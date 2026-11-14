using System.Runtime.InteropServices;

namespace Quai.App.Terminal;

/// <summary>
/// API C de <c>Microsoft.Terminal.Control.dll</c> : le moteur de Windows Terminal (analyse VT, tampon,
/// rendu DirectWrite / Direct3D « AtlasEngine »), dans une fenêtre Win32 enfant.
/// Signatures reprises de <c>src/cascadia/TerminalControl/HwndTerminal.hpp</c> (dépôt microsoft/terminal).
/// </summary>
internal static class TerminalNative
{
    public const string Library = "Microsoft.Terminal.Control.dll";

    /// <summary>Texte produit par le terminal (touches, souris, réponses VT) ; chaîne allouée par CoTaskMemAlloc, à libérer.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void WriteCallback(IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void ScrollCallback(int viewTop, int viewHeight, int bufferSize);

    /// <summary>Forme du curseur (<c>DispatchTypes::CursorStyle</c>).</summary>
    public enum CursorStyle : uint
    {
        BlinkingBlock = 1,
        SteadyBlock = 2,
        BlinkingUnderline = 3,
        SteadyUnderline = 4,
        BlinkingBar = 5,
        SteadyBar = 6,
    }

    [DllImport(Library, ExactSpelling = true)]
    public static extern void AvoidBuggyTSFConsoleFlags();

    /// <returns>HRESULT.</returns>
    [DllImport(Library, ExactSpelling = true)]
    public static extern int CreateTerminal(IntPtr parentHwnd, out IntPtr hwnd, out IntPtr terminal);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void DestroyTerminal(IntPtr terminal);

    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern void TerminalSendOutput(IntPtr terminal, string data);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void TerminalRegisterWriteCallback(IntPtr terminal, WriteCallback callback);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void TerminalRegisterScrollCallback(IntPtr terminal, ScrollCallback callback);

    /// <returns>HRESULT.</returns>
    [DllImport(Library, ExactSpelling = true)]
    public static extern int TerminalTriggerResize(IntPtr terminal, int width, int height, out TilSize dimensions);

    /// <returns>HRESULT.</returns>
    [DllImport(Library, ExactSpelling = true)]
    public static extern int TerminalCalculateResize(IntPtr terminal, int width, int height, out TilSize dimensions);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void TerminalDpiChanged(IntPtr terminal, int newDpi);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void TerminalUserScroll(IntPtr terminal, int viewTop);

    /// <summary>Texte sélectionné (la sélection est effacée) ; chaîne CoTaskMemAlloc, à libérer.</summary>
    [DllImport(Library, ExactSpelling = true)]
    public static extern IntPtr TerminalGetSelection(IntPtr terminal);

    [DllImport(Library, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool TerminalIsSelectionActive(IntPtr terminal);

    /// <param name="fontFamily">Liste de polices séparées par des virgules (la première trouvée sert de police principale).</param>
    /// <param name="fontSize">Taille en points.</param>
    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern void TerminalSetTheme(IntPtr terminal, TerminalTheme theme, string fontFamily, int fontSize, int newDpi);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void TerminalSendKeyEvent(IntPtr terminal, ushort vkey, ushort scanCode, ushort flags, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern void TerminalSendCharEvent(IntPtr terminal, char character, ushort scanCode, ushort flags);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void TerminalSetFocused(IntPtr terminal, [MarshalAs(UnmanagedType.I1)] bool focused);

    [StructLayout(LayoutKind.Sequential)]
    public struct TilSize
    {
        public int Width;
        public int Height;
    }

    /// <summary>Couleurs au format COLORREF (0x00BBGGRR), dans l'ordre de <c>TerminalTheme</c> (HwndTerminal.hpp).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TerminalTheme
    {
        public uint DefaultBackground;
        public uint DefaultForeground;
        public uint DefaultSelectionBackground;
        public CursorStyle CursorStyle;

        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U4, SizeConst = 16)]
        public uint[] ColorTable;
    }
}
