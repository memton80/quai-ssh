using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Quai.App.Services;
using Quai.Core.Localization;
using Quai.Core.Models;
using Quai.Core.Security;
using Quai.Core.Storage;
using Quai.Core.ViewModels;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Quai.App;

/// <summary>
/// Test de fumée lancé par le CI : <c>Quai.exe --smoke-test &lt;dossier&gt;</c>.
/// Ouvre la fenêtre, parcourt chaque page et les principaux écrans avec des données d'exemple
/// (dans un dossier de données jetable), enregistre des captures, puis quitte avec le code 0
/// (fichier <c>smoke-ok.txt</c>) ou 1 (fichier <c>smoke-error.txt</c>).
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

    /// <summary>À appeler avant la création des services : le test ne touche jamais aux vraies données.</summary>
    public static void IsolateData(string directory) =>
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, Path.Combine(directory, "data"));

    public static async Task RunAsync(MainWindow window, IServiceProvider services, ILogger logger, string directory)
    {
        var report = new List<string>();
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
                report.Add($"page {tag} : OK");
            }

            await ConnectionBookAsync(window, services, directory, report);
            CheckVault(services, report);

            File.WriteAllLines(Path.Combine(directory, "smoke-report.txt"), report);
            File.WriteAllText(Path.Combine(directory, "smoke-ok.txt"), "ok");
            logger.LogInformation("Test de fumée réussi");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test de fumée en échec");
            TryWrite(Path.Combine(directory, "smoke-report.txt"), string.Join(Environment.NewLine, report));
            Fail(directory, ex);
        }
    }

    public static void Fail(string directory, Exception? exception)
    {
        TryWrite(Path.Combine(directory, "smoke-error.txt"), exception?.ToString() ?? "Erreur inconnue");
        Environment.Exit(1);
    }

    /// <summary>Carnet rempli avec des connexions d'exemple, puis fiche de modification.</summary>
    private static async Task ConnectionBookAsync(MainWindow window, IServiceProvider services, string directory, List<string> report)
    {
        var repository = services.GetRequiredService<IConnectionRepository>();
        repository.Save(new ConnectionProfile
        {
            Name = "Frontal web",
            Host = "web01.example.org",
            Username = "deploy",
            Folder = "Production/Web",
            Tags = ["web", "nginx"],
        });
        repository.Save(new ConnectionProfile
        {
            Name = "Base de données",
            Host = "db.example.org",
            Port = 2222,
            Username = "postgres",
            Authentication = AuthenticationKind.PrivateKey,
            PrivateKeyPath = @"C:\Users\moi\.ssh\id_ed25519",
            Folder = "Production",
            Tags = ["db"],
        });
        repository.Save(new ConnectionProfile { Name = "Raspberry Pi", Host = "192.168.1.20", Username = "pi", Tags = ["maison"] });

        var viewModel = services.GetRequiredService<ConnectionsViewModel>();
        viewModel.Reload();
        window.NavigateTo(MainWindow.ConnectionsTag);
        await Task.Delay(800);
        await CaptureAsync(window.Root, Path.Combine(directory, "connections-filled.png"));
        if (viewModel.Connections.Count != 3 || viewModel.Filters.Count < 5)
        {
            throw new InvalidOperationException("Le carnet n'affiche pas les connexions d'exemple.");
        }

        report.Add("carnet rempli : OK");

        var dialogs = services.GetRequiredService<DialogService>();
        var editor = new ConnectionEditorViewModel(
            services.GetRequiredService<IStrings>(),
            repository.GetAll()[0],
            viewModel.GetKnownFolders());
        var pending = dialogs.EditConnectionAsync(editor);
        await Task.Delay(1200);
        var dialog = dialogs.Current ?? throw new InvalidOperationException("La fiche de connexion ne s'est pas ouverte.");
        await CaptureAsync(dialog, Path.Combine(directory, "connection-editor.png"));
        dialog.Hide();
        await pending;
        report.Add("fiche de connexion : OK");
    }

    /// <summary>Aller-retour dans le coffre Windows (informatif : dépend de la session du runner).</summary>
    private static void CheckVault(IServiceProvider services, List<string> report)
    {
        const string Key = "smoke-test/secret";
        try
        {
            var secrets = services.GetRequiredService<ISecretStore>();
            var value = Guid.NewGuid().ToString("N");
            secrets.Set(Key, value);
            var roundTrip = secrets.Get(Key) == value;
            secrets.Remove(Key);
            var removed = secrets.Get(Key) is null;
            report.Add(roundTrip && removed ? "coffre Windows : OK" : "coffre Windows : INCOHÉRENT");
        }
        catch (Exception ex)
        {
            report.Add($"coffre Windows : indisponible ({ex.GetType().Name} 0x{ex.HResult:X8})");
        }
    }

    private static void TryWrite(string path, string content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        catch (IOException)
        {
            // Rien de plus à faire : le code de sortie signale l'échec.
        }
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
