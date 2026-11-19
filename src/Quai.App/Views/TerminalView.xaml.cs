using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Quai.App.Localization;
using Quai.App.Terminal;
using Quai.Core.Models;
using Quai.Core.Storage;
using Quai.Core.Terminal;
using Quai.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using static Quai.App.NativeMethods;

namespace Quai.App.Views;

/// <summary>
/// Un terminal : le moteur natif de Windows Terminal (<c>Microsoft.Terminal.Control.dll</c>), dans une fenêtre
/// Win32 posée sur la zone que ce contrôle réserve, reliée à la session (SSH ou port série). Aucun navigateur embarqué.
/// </summary>
public sealed partial class TerminalView : UserControl
{
    // Fenêtre qui porte le contenu XAML à l'intérieur de la fenêtre principale.
    private const string XamlBridgeClass = "Microsoft.UI.Content.DesktopChildSiteBridge";

    // Durée du suivi image par image d'une animation du volet (les animations de WinUI durent moins de 400 ms).
    private static readonly TimeSpan FollowDuration = TimeSpan.FromMilliseconds(700);

    // Polices de repli toujours ajoutées après celle des réglages.
    private static readonly string[] FallbackFonts = [AppSettings.DefaultTerminalFontFamily, "Consolas"];

    private readonly ISettingsService _settings;
    private readonly ILogger _logger;
    private readonly MemoryStream _pending = new();
    private readonly Lock _pendingGate = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly TerminalModeTracker _modes = new();
    private readonly DispatcherQueueTimer _overlayTimer;
    private NativeTerminal? _native;
    private Window? _window;
    private XamlRoot? _xamlRoot;
    private IntPtr _windowHandle;
    private IntPtr _bridgeHandle;
    private int _dpi;
    private bool _attached;
    private bool _flushQueued;
    private bool _scrollQueued;
    private volatile bool _failed;
    private volatile bool _closed;
    private bool _connectRequested;
    private bool _following;
    private DateTime _followUntil;
    private bool _focusWhenShown;
    private bool _restoreFocus;
    private (int Top, int Height, int Size) _scroll;

    /// <summary><c>Ctrl+Maj+T</c> tapé dans le terminal : la page ouvre un terminal local.</summary>
    public event Action? NewLocalTerminalRequested;

    public TerminalView(TerminalTabViewModel viewModel)
    {
        ViewModel = viewModel;
        var services = App.Current.Services;
        _settings = services.GetRequiredService<ISettingsService>();
        _logger = services.GetRequiredService<ILogger<TerminalView>>();
        InitializeComponent();

        // Une fois un menu ou une info-bulle ouvert, sa fermeture ne relance pas toujours la mise en page.
        _overlayTimer = DispatcherQueue.CreateTimer();
        _overlayTimer.Interval = TimeSpan.FromMilliseconds(150);
        _overlayTimer.Tick += (_, _) => UpdatePlacement();

        ViewModel.Output += OnOutput;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _settings.Changed += OnSettingsChanged;
        TerminalAirspace.Changed += UpdatePlacement;
        TerminalAirspace.Moving += FollowAnimation;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => ApplyAppearance();
        UpdateBackground(_settings.Current.TerminalFluentBackground);
    }

    public TerminalTabViewModel ViewModel { get; }

    /// <summary>La fenêtre du terminal est affichée (test de fumée).</summary>
    internal bool IsTerminalShown => _native?.IsShown == true;

    /// <summary>Donne le focus clavier au terminal, dès qu'il est affiché.</summary>
    public void FocusTerminal()
    {
        if (_native is { IsShown: true } native)
        {
            native.Focus();
        }
        else
        {
            _focusWhenShown = true;
        }
    }

    /// <summary>Libère le moteur et sa fenêtre (onglet fermé).</summary>
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        ViewModel.Output -= OnOutput;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _settings.Changed -= OnSettingsChanged;
        TerminalAirspace.Changed -= UpdatePlacement;
        TerminalAirspace.Moving -= FollowAnimation;
        Detach();
        _native?.Dispose();
        _native = null;
    }

    /// <summary>Test de fumée : fenêtre native du terminal (capture d'écran).</summary>
    internal IntPtr TerminalWindowForTest => _native?.Handle ?? IntPtr.Zero;

    /// <summary>Test de fumée : frappe du texte comme au clavier.</summary>
    internal void TypeForTest(string text) => _native?.TypeForTest(text);

    /// <summary>Test de fumée : combinaison de touches reçue par le terminal comme au clavier.</summary>
    internal bool PressForTest(bool control, bool shift, bool alt, ushort key) => _native?.PressForTest(control, shift, alt, key) == true;

    /// <summary>Test de fumée : texte actuellement affiché par le terminal.</summary>
    internal string ReadScreenText() => _native?.ReadVisibleTextForTest() ?? string.Empty;

    /// <summary>
    /// Test de fumée : écart (pixels) entre la fenêtre native et la zone XAML qu'elle doit recouvrir,
    /// ou <c>null</c> si le terminal n'est pas affiché.
    /// </summary>
    internal int? PlacementErrorForTest()
    {
        if (_native is not { IsShown: true } native || XamlRoot is not { } root)
        {
            return null;
        }

        var area = ToPixels(
            TerminalHost.TransformToVisual(null).TransformBounds(new Rect(0, 0, TerminalHost.ActualWidth, TerminalHost.ActualHeight)),
            root.RasterizationScale,
            XamlOrigin());
        var (x, y, width, height) = native.Bounds;
        return new[]
        {
            Math.Abs(area.Left - x),
            Math.Abs(area.Top - y),
            Math.Abs(area.Right - area.Left - width),
            Math.Abs(area.Bottom - area.Top - height),
        }.Max();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_closed)
        {
            return;
        }

        EnsureTerminal();
        if (!_attached)
        {
            _attached = true;
            LayoutUpdated += OnLayoutUpdated;
            _xamlRoot = XamlRoot;
            if (_xamlRoot is not null)
            {
                _xamlRoot.Changed += OnXamlRootChanged;
            }

            _window = App.Current.MainWindow;
            if (_window is not null)
            {
                _window.Activated += OnWindowActivated;
            }
        }

        UpdatePlacement();
    }

    /// <summary>Onglet désélectionné ou retiré : le terminal est masqué, sa session et son historique restent.</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach();
        _native?.Hide();
    }

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        LayoutUpdated -= OnLayoutUpdated;
        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= OnXamlRootChanged;
            _xamlRoot = null;
        }

        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            _window = null;
        }

        _overlayTimer.Stop();
        StopFollowing();
    }

    /// <summary>Le contenu glisse (volet de navigation) : la fenêtre native suit à chaque image, puis se cale.</summary>
    private void FollowAnimation()
    {
        _followUntil = DateTime.UtcNow + FollowDuration;
        if (!_following && _attached && !_closed)
        {
            _following = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void OnRendering(object? sender, object e)
    {
        UpdatePlacement();
        if (DateTime.UtcNow >= _followUntil)
        {
            StopFollowing();
        }
    }

    private void StopFollowing()
    {
        if (_following)
        {
            _following = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void EnsureTerminal()
    {
        if (_native is not null || _failed || App.Current.MainWindow is not { } window)
        {
            return;
        }

        NativeTerminal native;
        try
        {
            _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            native = NativeTerminal.Create(_windowHandle, _logger);
        }
        catch (Exception ex)
        {
            // DLL absente ou incompatible, ou fenêtre impossible à créer.
            _failed = true;
            _logger.LogError(ex, "Le moteur de terminal n'a pas pu démarrer");
            EngineError.Message = Loc.Format("Terminal_EngineErrorMessage", ex.Message);
            EngineError.IsOpen = true;
            lock (_pendingGate)
            {
                _pending.SetLength(0);
            }

            return;
        }

        native.IsMouseTrackingActive = () => _modes.MouseTracking;
        native.Input += text => ViewModel.SendInput(text);
        native.Resized += (columns, rows) => ViewModel.Resize(columns, rows);
        native.Scrolled += OnTerminalScrolled;
        native.CopyRequested += CopySelection;
        native.PasteRequested += () => _ = PasteAsync();

        // Raccourci reçu dans la procédure de fenêtre du moteur : le dialogue s'ouvre juste après.
        native.SnippetsRequested += () => DispatcherQueue.TryEnqueue(() => _ = RunSnippetAsync());
        native.NewLocalTerminalRequested += () => DispatcherQueue.TryEnqueue(() => NewLocalTerminalRequested?.Invoke());

        // Raccourcis des snippets : traités dans la procédure de fenêtre du moteur (fil d'interface), envoi immédiat.
        var snippets = App.Current.Services.GetRequiredService<SnippetsViewModel>();
        native.ShortcutHandler = gesture =>
        {
            if (_closed || !snippets.TryRunShortcut(ViewModel, gesture))
            {
                return false;
            }

            native.ScrollTo(int.MaxValue);
            return true;
        };
        native.FocusChanged += OnTerminalFocusChanged;
        _native = native;
        ApplyAppearance();
    }

    private void OnLayoutUpdated(object? sender, object e) => UpdatePlacement();

    /// <summary>
    /// Pose la fenêtre du terminal sur <see cref="TerminalHost"/> (pixels physiques), la masque si la zone n'est
    /// pas visible ou si un dialogue recouvre la fenêtre, et découpe les menus et info-bulles ouverts par-dessus.
    /// </summary>
    private void UpdatePlacement()
    {
        if (_native is not { } native || _closed)
        {
            return;
        }

        if (!_attached || XamlRoot is not { } root || TerminalAirspace.IsModalOpen
            || TerminalHost.ActualWidth < 1 || TerminalHost.ActualHeight < 1 || !IsEffectivelyVisible(TerminalHost))
        {
            _overlayTimer.Stop();
            HideKeepingFocus(native);
            return;
        }

        var scale = root.RasterizationScale;
        var origin = XamlOrigin();
        var area = ToPixels(
            TerminalHost.TransformToVisual(null).TransformBounds(new Rect(0, 0, TerminalHost.ActualWidth, TerminalHost.ActualHeight)),
            scale,
            origin);

        var covered = new List<RECT>();
        var hidden = false;
        var overlays = false;
        foreach (var bounds in OverlayBounds(root))
        {
            overlays = true;
            var rect = ToPixels(bounds, scale, origin);
            if (rect.Left < area.Right && rect.Right > area.Left && rect.Top < area.Bottom && rect.Bottom > area.Top)
            {
                covered.Add(rect);
                hidden |= rect.Left <= area.Left && rect.Top <= area.Top && rect.Right >= area.Right && rect.Bottom >= area.Bottom;
            }
        }

        if (overlays && !_overlayTimer.IsRunning)
        {
            _overlayTimer.Start();
        }
        else if (!overlays)
        {
            _overlayTimer.Stop();
        }

        if (hidden)
        {
            HideKeepingFocus(native);
            return;
        }

        native.Show(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top, covered);

        if (!_connectRequested && native.Columns > 0)
        {
            // Le terminal connaît sa taille réelle : la session peut s'ouvrir avec les bonnes dimensions.
            _connectRequested = true;
            FlushPending();
            _ = ViewModel.ConnectAsync(native.Columns, native.Rows);
        }

        if (_focusWhenShown)
        {
            _focusWhenShown = false;
            native.Focus();
        }
    }

    /// <summary>Masqué alors qu'il avait le focus (dialogue ouvert par-dessus) : il le retrouvera en réapparaissant.</summary>
    private void HideKeepingFocus(NativeTerminal native)
    {
        if (native.HasFocus)
        {
            _focusWhenShown = true;
        }

        native.Hide();
    }

    private static IEnumerable<Rect> OverlayBounds(XamlRoot root)
    {
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } child)
            {
                yield return child.TransformToVisual(null).TransformBounds(new Rect(0, 0, child.ActualWidth, child.ActualHeight));
            }
        }

        foreach (var bounds in TerminalAirspace.Overlays())
        {
            yield return bounds;
        }
    }

    private static bool IsEffectivelyVisible(UIElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: Visibility.Collapsed } or UIElement { Opacity: <= 0 })
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Position du contenu XAML dans la zone cliente de la fenêtre principale.</summary>
    private POINT XamlOrigin()
    {
        if (_bridgeHandle == IntPtr.Zero)
        {
            _bridgeHandle = FindWindowExW(_windowHandle, IntPtr.Zero, XamlBridgeClass, null);
        }

        var origin = default(POINT);
        if (_bridgeHandle != IntPtr.Zero)
        {
            MapWindowPoints(_bridgeHandle, _windowHandle, ref origin, 1);
        }

        return origin;
    }

    private static RECT ToPixels(Rect bounds, double scale, POINT origin) => new()
    {
        Left = origin.X + (int)Math.Round(bounds.Left * scale),
        Top = origin.Y + (int)Math.Round(bounds.Top * scale),
        Right = origin.X + (int)Math.Round(bounds.Right * scale),
        Bottom = origin.Y + (int)Math.Round(bounds.Bottom * scale),
    };

    /// <summary>Changement d'écran ou d'échelle : le moteur reçoit la nouvelle résolution.</summary>
    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (CurrentDpi() != _dpi)
        {
            ApplyAppearance();
        }

        UpdatePlacement();
    }

    private int CurrentDpi() => XamlRoot is { } root
        ? (int)Math.Round(root.RasterizationScale * 96)
        : (int)GetDpiForWindow(_windowHandle);

    /// <summary>Thème, police et taille du terminal (réglages), appliqués à chaud.</summary>
    private void ApplyAppearance()
    {
        var appearance = _settings.Current;
        UpdateBackground(appearance.TerminalFluentBackground);
        if (_native is null || _closed)
        {
            return;
        }

        _dpi = CurrentDpi();
        _native.SetAppearance(
            TerminalPalette.For(ActualTheme, appearance.TerminalFluentBackground),
            FontList(appearance.TerminalFontFamily),
            appearance.TerminalFontSize,
            _dpi);
    }

    /// <summary>La police choisie, puis des polices de repli à chasse fixe présentes sur Windows.</summary>
    private static string FontList(string family)
    {
        var fonts = family.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (var fallback in FallbackFonts)
        {
            if (!fonts.Contains(fallback, StringComparer.OrdinalIgnoreCase))
            {
                fonts.Add(fallback);
            }
        }

        return string.Join(", ", fonts);
    }

    private void UpdateBackground(bool fluent) =>
        TerminalArea.Background = new SolidColorBrush(TerminalPalette.Background(ActualTheme, fluent));

    /// <summary>Réglages modifiés (éventuellement depuis un autre fil).</summary>
    private void OnSettingsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(ApplyAppearance);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalTabViewModel.Status) && ViewModel.IsConnecting)
        {
            // Nouvelle session : le serveur n'a encore activé aucun mode.
            _modes.Reset();
        }
    }

    private void OnAreaPointerPressed(object sender, PointerRoutedEventArgs e) => FocusTerminal();

    /// <summary><c>Ctrl+Maj+P</c> ou bouton « Snippets » : choix d'un snippet, envoyé comme s'il était tapé.</summary>
    public async Task RunSnippetAsync()
    {
        if (_closed)
        {
            return;
        }

        try
        {
            if (await App.Current.Services.GetRequiredService<SnippetsViewModel>().RunInAsync(ViewModel))
            {
                _native?.ScrollTo(int.MaxValue);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Envoi d'un snippet impossible");
        }

        FocusTerminal();
    }

    /// <summary>Connexion rapide : fiche de création préremplie, puis l'onglet adopte la connexion enregistrée.</summary>
    private async void OnSaveToBookClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var connections = App.Current.Services.GetRequiredService<ConnectionsViewModel>();
            var saved = await connections.SaveQuickConnectionAsync(ViewModel.Profile);
            if (saved is not null)
            {
                ViewModel.AdoptSavedProfile(saved);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ajout de la connexion rapide au carnet impossible");
        }

        FocusTerminal();
    }

    private void OnSaveOfferClosed(InfoBar sender, object args) => ViewModel.SaveOfferDismissed = true;

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        // Retour dans l'application : le terminal retrouve le focus qu'il avait en la quittant.
        if (args.WindowActivationState != WindowActivationState.Deactivated && _restoreFocus && _native is { IsShown: true })
        {
            _restoreFocus = false;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _native?.Focus());
        }
    }

    private void OnTerminalFocusChanged(bool focused, bool leftApplication) =>
        _restoreFocus = !focused && leftApplication;

    private void OnTerminalScrolled(int top, int height, int size)
    {
        _scroll = (top, height, size);
        if (_scrollQueued)
        {
            return;
        }

        // Appelé pendant le traitement de la sortie par le moteur : la barre est mise à jour juste après.
        _scrollQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _scrollQueued = false;
            var (viewTop, viewHeight, bufferSize) = _scroll;
            var maximum = Math.Max(0, bufferSize - viewHeight);
            HistoryScrollBar.Maximum = maximum;
            HistoryScrollBar.ViewportSize = Math.Max(1, viewHeight);
            HistoryScrollBar.LargeChange = Math.Max(1, viewHeight);
            HistoryScrollBar.SmallChange = 1;
            HistoryScrollBar.Value = Math.Clamp(viewTop, 0, maximum);
            HistoryScrollBar.IsEnabled = maximum > 0;
        });
    }

    private void OnScrollBarScroll(object sender, ScrollEventArgs e) => _native?.ScrollTo((int)Math.Round(e.NewValue));

    private void CopySelection()
    {
        var text = _native?.TakeSelection();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            // Presse-papiers verrouillé par une autre application.
            _logger.LogWarning("Copie impossible ({Error})", ex.GetType().Name);
        }
    }

    private async Task PasteAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                return;
            }

            var text = await content.GetTextAsync();
            if (!string.IsNullOrEmpty(text) && !_closed)
            {
                _native?.ScrollTo(int.MaxValue);
                ViewModel.SendInput(PasteFormatter.Format(text, _modes.BracketedPaste));
            }
        }
        catch (Exception ex)
        {
            // Presse-papiers verrouillé par une autre application : on ne colle rien.
            _logger.LogWarning("Collage impossible ({Error})", ex.GetType().Name);
        }
    }

    /// <summary>Sortie de la session, reçue hors du fil d'interface : mise en tampon puis envoi groupé.</summary>
    private void OnOutput(byte[] data)
    {
        if (_failed || _closed)
        {
            return;
        }

        lock (_pendingGate)
        {
            _pending.Write(data, 0, data.Length);
            if (_flushQueued)
            {
                return;
            }

            _flushQueued = true;
        }

        if (!DispatcherQueue.TryEnqueue(FlushPending))
        {
            lock (_pendingGate)
            {
                _flushQueued = false;
            }
        }
    }

    private void FlushPending()
    {
        var native = _native;
        byte[] data;
        lock (_pendingGate)
        {
            _flushQueued = false;
            if (_closed || native is not { Columns: > 0 } || _pending.Length == 0)
            {
                // Terminal pas encore dimensionné : le tampon est conservé et vidé au premier affichage.
                return;
            }

            data = _pending.ToArray();
            _pending.SetLength(0);
        }

        // Décodage UTF-8 avec état : un caractère coupé entre deux paquets est recollé.
        var characters = new char[_decoder.GetCharCount(data, 0, data.Length, flush: false)];
        var count = _decoder.GetChars(data, 0, data.Length, characters, 0, flush: false);
        var text = new string(characters, 0, count);
        if (text.Contains('\0', StringComparison.Ordinal))
        {
            // Le moteur reçoit une chaîne C : un NUL la couperait (et les terminaux l'ignorent).
            text = text.Replace("\0", string.Empty, StringComparison.Ordinal);
        }

        _modes.Feed(text);
        native.SendOutput(text);
    }
}
