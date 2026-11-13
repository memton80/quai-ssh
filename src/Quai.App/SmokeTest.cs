using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Quai.App;

/// <summary>
/// Test de fumée lancé par le CI : <c>Quai.exe --smoke-test &lt;dossier&gt;</c>.
/// Ouvre la fenêtre, parcourt chaque page, enregistre une capture par page, puis quitte
/// avec le code 0 (fichier <c>smoke-ok.txt</c>) ou 1 (fichier <c>smoke-error.txt</c>).
/// </summary>
internal static class SmokeTest
{
    private const string Switch = "--smoke-test";

    public static bool TryGetOutputDirectory(out string directory)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.FindIndex(args, a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            directory = string.Empty;
            return false;
        }

        directory = index + 1 < args.Length
            ? Path.GetFullPath(args[index + 1])
            : Path.Combine(Path.GetTempPath(), "quai-smoke");
        return true;
    }

    public static async Task RunAsync(MainWindow window, ILogger logger, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            await Task.Delay(1500);

            foreach (var tag in MainWindow.PageTags)
            {
                logger.LogInformation("Test de fumée : page {Page}", tag);
                window.NavigateTo(tag);
                await Task.Delay(800);
                await CaptureAsync(window.Root, Path.Combine(directory, $"{tag}.png"));
            }

            File.WriteAllText(Path.Combine(directory, "smoke-ok.txt"), "ok");
            logger.LogInformation("Test de fumée réussi");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test de fumée en échec");
            Fail(directory, ex);
        }
    }

    public static void Fail(string directory, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "smoke-error.txt"), exception?.ToString() ?? "Erreur inconnue");
        }
        catch (IOException)
        {
            // Rien de plus à faire : le code de sortie signale l'échec.
        }

        Environment.Exit(1);
    }

    private static async Task CaptureAsync(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
        {
            return;
        }

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth,
            (uint)bitmap.PixelHeight,
            96,
            96,
            pixels);
        await encoder.FlushAsync();

        stream.Seek(0);
        await using var file = File.Create(path);
        await stream.AsStreamForRead().CopyToAsync(file);
    }
}
