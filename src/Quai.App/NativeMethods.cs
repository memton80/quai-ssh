using System.Runtime.InteropServices;

namespace Quai.App;

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);
}
