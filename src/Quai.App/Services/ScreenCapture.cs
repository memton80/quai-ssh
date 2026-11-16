using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using static Quai.App.NativeMethods;

namespace Quai.App.Services;

/// <summary>
/// Capture d'une fenêtre telle qu'elle est affichée à l'écran (test de fumée) : contrairement à
/// <c>RenderTargetBitmap</c>, elle montre aussi les fenêtres natives posées sur le XAML (terminal).
/// </summary>
internal static class ScreenCapture
{
    /// <returns>Nombre de couleurs distinctes : une capture uniforme (1) signale un rendu vide.</returns>
    public static async Task<int> SaveAsync(IntPtr hwnd, string path)
    {
        if (!GetWindowRect(hwnd, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            return 0;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var pixels = Capture(rect.Left, rect.Top, width, height);

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        await using (var file = File.Create(path))
        {
            await stream.AsStreamForRead().CopyToAsync(file);
        }

        var colors = new HashSet<int>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            colors.Add(pixels[i] | (pixels[i + 1] << 8) | (pixels[i + 2] << 16));
        }

        return colors.Count;
    }

    /// <summary>Couleur la plus présente dans la fenêtre telle qu'affichée (fond du terminal), ou <c>null</c>.</summary>
    public static Windows.UI.Color? DominantColor(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            return null;
        }

        var pixels = Capture(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        var counts = new Dictionary<int, int>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var color = pixels[i] | (pixels[i + 1] << 8) | (pixels[i + 2] << 16);
            counts[color] = counts.GetValueOrDefault(color) + 1;
        }

        var top = counts.MaxBy(pair => pair.Value).Key;
        return Windows.UI.Color.FromArgb(255, (byte)(top >> 16), (byte)(top >> 8), (byte)top);
    }

    private static byte[] Capture(int x, int y, int width, int height)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // lignes de haut en bas
            biPlanes = 1,
            biBitCount = 32,
        };
        var bitmap = CreateDIBSection(screen, ref header, 0, out var bits, IntPtr.Zero, 0);
        var previous = SelectObject(memory, bitmap);
        try
        {
            var pixels = new byte[width * height * 4];
            if (bitmap != IntPtr.Zero && BitBlt(memory, 0, 0, width, height, screen, x, y, SRCCOPY | CAPTUREBLT))
            {
                Marshal.Copy(bits, pixels, 0, pixels.Length);
            }

            return pixels;
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
