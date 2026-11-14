using System.Runtime.InteropServices;

namespace Quai.App;

/// <summary>Fonctions Win32 (fenêtres enfants du terminal natif, captures du test de fumée).</summary>
internal static class NativeMethods
{
    public const int GWLP_WNDPROC = -4;
    public const uint GA_ROOT = 2;

    public const uint WS_CHILD = 0x40000000;
    public const uint WS_CLIPSIBLINGS = 0x04000000;
    public const uint WS_CLIPCHILDREN = 0x02000000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;

    public const int SW_HIDE = 0;

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_NCDESTROY = 0x0082;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;
    public const uint WM_SYSCHAR = 0x0106;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MOUSEWHEEL = 0x020A;

    public const int MA_ACTIVATE = 1;
    public const int MK_LBUTTON = 0x0001;

    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12;
    public const ushort VK_PRIOR = 0x21;
    public const ushort VK_NEXT = 0x22;
    public const ushort VK_END = 0x23;
    public const ushort VK_HOME = 0x24;
    public const ushort VK_UP = 0x26;
    public const ushort VK_DOWN = 0x28;
    public const ushort VK_INSERT = 0x2D;
    public const ushort VK_SPACE = 0x20;
    public const ushort VK_F4 = 0x73;

    public const uint SPI_GETWHEELSCROLLLINES = 0x0068;

    public const int RGN_DIFF = 4;
    public const uint SRCCOPY = 0x00CC0020;
    public const uint CAPTUREBLT = 0x40000000;

    public delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(
        uint exStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern IntPtr CallWindowProcW(IntPtr previous, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref POINT points, uint count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfoW(uint action, uint param, out uint value, uint winIni);

    [DllImport("user32.dll")]
    public static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    public static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint rop);

    /// <summary>Coordonnées d'un message souris, comme <c>MAKELPARAM</c>.</summary>
    public static IntPtr MakeLParam(int low, int high) => (IntPtr)((high << 16) | (low & 0xFFFF));

    public static bool IsKeyDown(int virtualKey) => GetKeyState(virtualKey) < 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }
}
