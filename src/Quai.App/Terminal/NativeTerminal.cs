using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using static Quai.App.NativeMethods;

namespace Quai.App.Terminal;

/// <summary>
/// Une instance du moteur de Windows Terminal dans la fenêtre principale.
/// Une fenêtre conteneur, posée sur la zone que le XAML réserve au terminal, contient la fenêtre du moteur ;
/// celle-ci est sous-classée pour le clavier, le focus, la molette et le presse-papiers.
/// Tout se passe sur le fil d'interface.
/// </summary>
internal sealed class NativeTerminal : IDisposable
{
    private const string HostClassName = "QuaiTerminalHost";
    private const int ErrorClassAlreadyExists = 1410;
    private const int WheelDelta = 120;
    private const int DefaultWheelLines = 3;

    // Délégués statiques : jamais collectés tant que des fenêtres les utilisent.
    private static readonly WndProc HostProcedure = HostWindowProcedure;
    private static readonly WndProc TerminalProcedure = TerminalWindowProcedure;
    private static readonly Dictionary<IntPtr, NativeTerminal> Instances = [];
    private static IntPtr _originalTerminalProcedure;
    private static bool _initialized;

    // Gardés en champ : le moteur les appelle tant que l'instance existe.
    private readonly TerminalNative.WriteCallback _writeCallback;
    private readonly TerminalNative.ScrollCallback _scrollCallback;
    private readonly ILogger _logger;
    private IntPtr _host;
    private IntPtr _hwnd;
    private IntPtr _terminal;
    private Appearance? _appearance;
    private bool _appearanceApplied;
    private int _x;
    private int _y;
    private int _width;
    private int _height;
    private string _region = string.Empty;
    private bool _shown;
    private bool _disposing;
    private bool _swallowChar;
    private bool _passSystemChar;
    private int _wheelRemainder;
    private int _viewTop;
    private int _viewHeight;
    private int _bufferSize;

    private NativeTerminal(ILogger logger)
    {
        _logger = logger;
        _writeCallback = OnWrite;
        _scrollCallback = OnScroll;
    }

    /// <summary>Texte à envoyer au serveur : frappes, rapports de souris, réponses aux requêtes VT.</summary>
    public event Action<string>? Input;

    /// <summary>Nouvelles dimensions en caractères (colonnes, lignes).</summary>
    public event Action<int, int>? Resized;

    /// <summary>Position de la vue dans l'historique (première ligne visible, hauteur, taille du tampon).</summary>
    public event Action<int, int, int>? Scrolled;

    public event Action? CopyRequested;

    public event Action? PasteRequested;

    /// <summary><c>Ctrl+Maj+P</c> : choisir un snippet à envoyer.</summary>
    public event Action? SnippetsRequested;

    /// <summary>Focus clavier gagné ou perdu ; le second argument indique une perte au profit d'une autre application.</summary>
    public event Action<bool, bool>? FocusChanged;

    /// <summary>L'application distante suit la souris : clics et molette lui reviennent.</summary>
    public Func<bool>? IsMouseTrackingActive { get; set; }

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    /// <summary>La fenêtre du terminal est affichée (pas masquée par la navigation ou un dialogue).</summary>
    public bool IsShown => _shown;

    public bool HasFocus => _hwnd != IntPtr.Zero && GetFocus() == _hwnd;

    /// <summary>Fenêtre conteneur (captures du test de fumée).</summary>
    public IntPtr Handle => _host;

    /// <summary>Position et taille actuelles (pixels, coordonnées client de la fenêtre principale).</summary>
    public (int X, int Y, int Width, int Height) Bounds => (_x, _y, _width, _height);

    /// <summary>Crée le terminal, masqué, dans la fenêtre principale <paramref name="parent"/>.</summary>
    public static NativeTerminal Create(IntPtr parent, ILogger logger)
    {
        EnsureInitialized();
        var terminal = new NativeTerminal(logger);
        try
        {
            terminal.Initialize(parent);
        }
        catch
        {
            terminal.Dispose();
            throw;
        }

        return terminal;
    }

    /// <summary>Police, taille (points), couleurs et échelle ; appliquées dès que le terminal a une taille.</summary>
    public void SetAppearance(TerminalNative.TerminalTheme theme, string fontFamily, int fontSize, int dpi)
    {
        _appearance = new Appearance(theme, fontFamily, fontSize, dpi);
        _appearanceApplied = false;
        if (_width > 0 && _height > 0)
        {
            ApplyAppearance();
        }
    }

    /// <summary>
    /// Place le terminal (pixels physiques, coordonnées client de la fenêtre principale) et découpe les zones
    /// recouvertes par des éléments XAML flottants (menus, info-bulles, volet de navigation).
    /// </summary>
    public void Show(int x, int y, int width, int height, IReadOnlyList<RECT> covered)
    {
        if (_host == IntPtr.Zero || width <= 0 || height <= 0)
        {
            Hide();
            return;
        }

        if (!_shown || x != _x || y != _y || width != _width || height != _height)
        {
            SetWindowPos(_host, IntPtr.Zero, x, y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            _x = x;
            _y = y;
            _shown = true;
        }

        ApplyRegion(width, height, covered);

        if (width != _width || height != _height)
        {
            _width = width;
            _height = height;
            if (!_appearanceApplied && _appearance is not null)
            {
                // Premier dimensionnement : la police est appliquée sur une fenêtre déjà à sa taille.
                Resize();
                ApplyAppearance();
            }
            else
            {
                Resize();
            }
        }
    }

    public void Hide()
    {
        if (_shown && _host != IntPtr.Zero)
        {
            SetWindowPos(_host, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_HIDEWINDOW);
        }

        _shown = false;
    }

    public void Focus()
    {
        if (_shown && !_disposing)
        {
            SetFocus(_hwnd);
        }
    }

    /// <summary>Texte reçu du serveur (déjà décodé de l'UTF-8).</summary>
    public void SendOutput(string text)
    {
        if (_terminal != IntPtr.Zero && !_disposing && text.Length > 0)
        {
            TerminalNative.TerminalSendOutput(_terminal, text);
        }
    }

    /// <summary>Fait défiler l'historique (barre de défilement, clavier, molette).</summary>
    public void ScrollTo(int viewTop)
    {
        if (_terminal == IntPtr.Zero || _disposing)
        {
            return;
        }

        var top = Math.Clamp(viewTop, 0, Math.Max(0, _bufferSize - _viewHeight));
        if (top == _viewTop)
        {
            return;
        }

        TerminalNative.TerminalUserScroll(_terminal, top);
        _viewTop = top;
        Scrolled?.Invoke(_viewTop, _viewHeight, _bufferSize);
    }

    /// <summary>Texte sélectionné à la souris (la sélection est ensuite effacée), ou <c>null</c>.</summary>
    public string? TakeSelection()
    {
        if (_terminal == IntPtr.Zero || _disposing || !TerminalNative.TerminalIsSelectionActive(_terminal))
        {
            return null;
        }

        var text = TerminalNative.TerminalGetSelection(_terminal);
        try
        {
            return Marshal.PtrToStringUni(text);
        }
        finally
        {
            Marshal.FreeCoTaskMem(text);
        }
    }

    /// <summary>Test de fumée : frappe du texte comme au clavier (messages WM_CHAR).</summary>
    public void TypeForTest(string text)
    {
        foreach (var character in text)
        {
            SendMessageW(_hwnd, WM_CHAR, character, 1);
        }
    }

    /// <summary>Test de fumée : sélectionne toute la zone visible à la souris et retourne son texte.</summary>
    public string ReadVisibleTextForTest()
    {
        if (!_shown || _width < 4 || _height < 4)
        {
            return string.Empty;
        }

        SendMessageW(_hwnd, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(1, 1));
        SendMessageW(_hwnd, WM_MOUSEMOVE, MK_LBUTTON, MakeLParam(_width - 2, _height - 2));
        SendMessageW(_hwnd, WM_LBUTTONUP, IntPtr.Zero, MakeLParam(_width - 2, _height - 2));
        return TakeSelection() ?? string.Empty;
    }

    public void Dispose()
    {
        if (_disposing)
        {
            return;
        }

        // Le focus quitte le terminal tant que le moteur est intact (la destruction de la fenêtre le notifierait).
        if (HasFocus)
        {
            SetFocus(GetAncestor(_host, GA_ROOT));
        }

        _disposing = true;
        if (_terminal != IntPtr.Zero)
        {
            TerminalNative.DestroyTerminal(_terminal);
            _terminal = IntPtr.Zero;
        }

        if (_hwnd != IntPtr.Zero)
        {
            Instances.Remove(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_host != IntPtr.Zero)
        {
            DestroyWindow(_host);
            _host = IntPtr.Zero;
        }

        _shown = false;
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        // Comme dans le contrôle WPF de Windows Terminal : le fil d'interface utilise déjà TSF (zones de texte XAML),
        // le mode « console » de TSF n'y est pas activé.
        TerminalNative.AvoidBuggyTSFConsoleFlags();

        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(HostProcedure),
            hInstance = GetModuleHandleW(null),
            lpszClassName = HostClassName,
        };
        if (RegisterClassExW(ref windowClass) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error);
            }
        }

        _initialized = true;
    }

    private void Initialize(IntPtr parent)
    {
        _host = CreateWindowExW(
            0,
            HostClassName,
            null,
            WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0,
            0,
            0,
            0,
            parent,
            IntPtr.Zero,
            GetModuleHandleW(null),
            IntPtr.Zero);
        if (_host == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var result = TerminalNative.CreateTerminal(_host, out _hwnd, out _terminal);
        if (result < 0)
        {
            _hwnd = IntPtr.Zero;
            _terminal = IntPtr.Zero;
            Marshal.ThrowExceptionForHR(result);
        }

        Instances[_hwnd] = this;
        var previous = SetWindowLongPtrW(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(TerminalProcedure));
        if (_originalTerminalProcedure == IntPtr.Zero)
        {
            // Toutes les fenêtres du moteur partagent la même procédure de classe.
            _originalTerminalProcedure = previous;
        }

        TerminalNative.TerminalRegisterWriteCallback(_terminal, _writeCallback);
        TerminalNative.TerminalRegisterScrollCallback(_terminal, _scrollCallback);
    }

    private void ApplyAppearance()
    {
        if (_appearance is not { } appearance || _terminal == IntPtr.Zero || _disposing)
        {
            return;
        }

        TerminalNative.TerminalSetTheme(_terminal, appearance.Theme, appearance.FontFamily, appearance.FontSize, appearance.Dpi);
        _appearanceApplied = true;

        // Nouvelle police : le nombre de colonnes et de lignes change pour la même surface.
        if (_width > 0 && _height > 0
            && TerminalNative.TerminalCalculateResize(_terminal, _width, _height, out var size) >= 0)
        {
            Report(size);
        }
    }

    private void Resize()
    {
        if (_terminal != IntPtr.Zero && !_disposing
            && TerminalNative.TerminalTriggerResize(_terminal, _width, _height, out var size) >= 0)
        {
            Report(size);
        }
    }

    private void Report(TerminalNative.TilSize size)
    {
        var columns = Math.Max(1, size.Width);
        var rows = Math.Max(1, size.Height);
        if (columns != Columns || rows != Rows)
        {
            Columns = columns;
            Rows = rows;
            Resized?.Invoke(columns, rows);
        }
    }

    /// <summary>Retire de la fenêtre les rectangles recouverts par du XAML flottant (coordonnées locales).</summary>
    private void ApplyRegion(int width, int height, IReadOnlyList<RECT> covered)
    {
        var parts = new List<RECT>();
        foreach (var rect in covered)
        {
            var clipped = new RECT
            {
                Left = Math.Max(0, rect.Left - _x),
                Top = Math.Max(0, rect.Top - _y),
                Right = Math.Min(width, rect.Right - _x),
                Bottom = Math.Min(height, rect.Bottom - _y),
            };
            if (clipped.Right > clipped.Left && clipped.Bottom > clipped.Top)
            {
                parts.Add(clipped);
            }
        }

        var key = parts.Count == 0
            ? string.Empty
            : $"{width}x{height}:" + string.Join(';', parts.Select(r => $"{r.Left},{r.Top},{r.Right},{r.Bottom}"));
        if (key == _region)
        {
            return;
        }

        _region = key;
        if (parts.Count == 0)
        {
            SetWindowRgn(_host, IntPtr.Zero, true);
            return;
        }

        var region = CreateRectRgn(0, 0, width, height);
        foreach (var part in parts)
        {
            var hole = CreateRectRgn(part.Left, part.Top, part.Right, part.Bottom);
            CombineRgn(region, region, hole, RGN_DIFF);
            DeleteObject(hole);
        }

        // En cas de succès, la région appartient au système.
        if (SetWindowRgn(_host, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private void OnWrite(IntPtr data)
    {
        try
        {
            var text = Marshal.PtrToStringUni(data);
            if (!string.IsNullOrEmpty(text))
            {
                Input?.Invoke(text);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saisie du terminal non transmise");
        }
        finally
        {
            Marshal.FreeCoTaskMem(data);
        }
    }

    private void OnScroll(int viewTop, int viewHeight, int bufferSize)
    {
        _viewTop = viewTop;
        _viewHeight = viewHeight;
        _bufferSize = bufferSize;
        try
        {
            Scrolled?.Invoke(viewTop, viewHeight, bufferSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Défilement du terminal non transmis");
        }
    }

    private static IntPtr HostWindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WM_ERASEBKGND:
                // Entièrement couverte par le terminal : rien à peindre.
                return 1;
            case WM_SETFOCUS:
                var child = FindWindowExW(hwnd, IntPtr.Zero, null, null);
                if (child != IntPtr.Zero)
                {
                    SetFocus(child);
                }

                return IntPtr.Zero;
            default:
                return DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    private static IntPtr TerminalWindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (Instances.TryGetValue(hwnd, out var instance))
        {
            try
            {
                if (instance.TryHandle(message, wParam, lParam, out var result))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                // Une exception ne doit jamais traverser un appel natif.
                instance._logger.LogError(ex, "Message {Message:X4} du terminal non traité", message);
            }
        }

        if (message == WM_NCDESTROY)
        {
            Instances.Remove(hwnd);
        }

        return CallWindowProcW(_originalTerminalProcedure, hwnd, message, wParam, lParam);
    }

    /// <returns><c>true</c> si le message est entièrement traité ici (il n'atteint pas le moteur).</returns>
    private bool TryHandle(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (_disposing || _terminal == IntPtr.Zero)
        {
            return false;
        }

        switch (message)
        {
            case WM_DESTROY:
                // Fenêtre détruite avec la fenêtre principale (fermeture de l'application) : le moteur libère
                // son rendu, plus aucun appel ne doit l'atteindre.
                _disposing = true;
                _shown = false;
                return false;

            case WM_SETFOCUS:
                TerminalNative.TerminalSetFocused(_terminal, true);
                FocusChanged?.Invoke(true, false);
                return false;

            case WM_KILLFOCUS:
                TerminalNative.TerminalSetFocused(_terminal, false);
                var root = GetAncestor(_hwnd, GA_ROOT);
                var leftApplication = wParam == IntPtr.Zero || GetAncestor(wParam, GA_ROOT) != root;
                FocusChanged?.Invoke(false, leftApplication);
                return false;

            case WM_MOUSEACTIVATE:
                SetFocus(_hwnd);
                result = MA_ACTIVATE;
                return true;

            case WM_LBUTTONDOWN:
            case WM_MBUTTONDOWN:
                TakeFocus();
                return false;

            case WM_RBUTTONDOWN:
                TakeFocus();
                if (MouseTracking)
                {
                    return false;
                }

                // Comme Windows Terminal : clic droit = copier la sélection, sinon coller.
                if (TerminalNative.TerminalIsSelectionActive(_terminal))
                {
                    CopyRequested?.Invoke();
                }
                else
                {
                    PasteRequested?.Invoke();
                }

                return true;

            case WM_RBUTTONUP:
                return !MouseTracking;

            case WM_MOUSEWHEEL:
                if (MouseTracking)
                {
                    return false;
                }

                ScrollByWheel((short)((long)wParam >> 16));
                return true;

            case WM_KEYDOWN:
            case WM_SYSKEYDOWN:
                return OnKeyDown(message, wParam, lParam);

            case WM_KEYUP:
            case WM_SYSKEYUP:
                Unpack(lParam, out var upScanCode, out var upFlags);
                TerminalNative.TerminalSendKeyEvent(_terminal, (ushort)wParam, upScanCode, upFlags, false);
                return true;

            case WM_CHAR:
                if (_swallowChar)
                {
                    // Caractère de contrôle produit par un raccourci déjà traité (Ctrl+C, Ctrl+V).
                    _swallowChar = false;
                    return true;
                }

                Unpack(lParam, out var scanCode, out var flags);
                TerminalNative.TerminalSendCharEvent(_terminal, (char)wParam, scanCode, flags);
                return true;

            case WM_SYSCHAR:
                if (_passSystemChar)
                {
                    // Alt+Espace : menu système de la fenêtre.
                    _passSystemChar = false;
                    return false;
                }

                // Les combinaisons Alt+touche sont déjà parties avec WM_SYSKEYDOWN ; pas de bip.
                return true;

            default:
                return false;
        }
    }

    private bool MouseTracking => IsMouseTrackingActive?.Invoke() == true;

    private bool OnKeyDown(uint message, IntPtr wParam, IntPtr lParam)
    {
        var key = (ushort)wParam;
        _swallowChar = false;
        var control = IsKeyDown(VK_CONTROL);
        var shift = IsKeyDown(VK_SHIFT);
        var alt = IsKeyDown(VK_MENU);

        // Ctrl+Alt = AltGr sur les claviers européens : jamais un raccourci de l'application.
        if (control && !alt)
        {
            var selection = TerminalNative.TerminalIsSelectionActive(_terminal);
            switch (key)
            {
                // Ctrl+C copie s'il y a une sélection, sinon il interrompt (^C) ; Ctrl+Maj+C copie toujours.
                case 'C' when selection || shift:
                case VK_INSERT:
                    if (selection)
                    {
                        CopyRequested?.Invoke();
                    }

                    _swallowChar = key == 'C';
                    return true;

                case 'V':
                    PasteRequested?.Invoke();
                    _swallowChar = true;
                    return true;

                // Ctrl+Maj+P (comme la palette de commandes de Windows Terminal) ; Ctrl+P seul reste au shell.
                case 'P' when shift:
                    SnippetsRequested?.Invoke();
                    _swallowChar = true;
                    return true;

                case VK_UP when shift:
                    ScrollTo(_viewTop - 1);
                    return true;

                case VK_DOWN when shift:
                    ScrollTo(_viewTop + 1);
                    return true;

                case VK_PRIOR when shift:
                    ScrollTo(_viewTop - Math.Max(1, _viewHeight));
                    return true;

                case VK_NEXT when shift:
                    ScrollTo(_viewTop + Math.Max(1, _viewHeight));
                    return true;

                case VK_HOME when shift:
                    ScrollTo(0);
                    return true;

                case VK_END when shift:
                    ScrollTo(int.MaxValue);
                    return true;
            }
        }

        if (shift && !control && !alt && key == VK_INSERT)
        {
            PasteRequested?.Invoke();
            return true;
        }

        // Alt+F4 ferme la fenêtre, Alt+Espace ouvre son menu système : traitement Windows habituel.
        if (message == WM_SYSKEYDOWN && alt && !control && key is VK_F4 or VK_SPACE)
        {
            _passSystemChar = key == VK_SPACE;
            return false;
        }

        Unpack(lParam, out var scanCode, out var flags);
        TerminalNative.TerminalSendKeyEvent(_terminal, key, scanCode, flags, true);
        return true;
    }

    private void TakeFocus()
    {
        if (GetFocus() != _hwnd)
        {
            SetFocus(_hwnd);
        }
    }

    private void ScrollByWheel(int delta)
    {
        if (!SystemParametersInfoW(SPI_GETWHEELSCROLLLINES, 0, out var lines, 0) || lines == 0)
        {
            lines = DefaultWheelLines;
        }

        _wheelRemainder += delta;
        var steps = _wheelRemainder / WheelDelta;
        if (steps == 0)
        {
            return;
        }

        _wheelRemainder -= steps * WheelDelta;

        // Valeur spéciale (uint.MaxValue) : une page par cran.
        var perStep = lines == uint.MaxValue ? Math.Max(1, _viewHeight) : (int)Math.Min(lines, 100);
        ScrollTo(_viewTop - (steps * perStep));
    }

    /// <summary>Code de balayage et indicateurs (touche étendue…) d'un message clavier.</summary>
    private static void Unpack(IntPtr lParam, out ushort scanCode, out ushort flags)
    {
        var high = (ulong)lParam >> 16 & 0xFFFF;
        scanCode = (ushort)(high & 0x00FF);
        flags = (ushort)(high & 0xFF00);
    }

    private sealed record Appearance(TerminalNative.TerminalTheme Theme, string FontFamily, int FontSize, int Dpi);
}
