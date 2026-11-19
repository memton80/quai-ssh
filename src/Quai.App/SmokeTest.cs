using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Quai.App.Localization;
using Quai.App.Services;
using Quai.App.Terminal;
using Quai.Core.Localization;
using Quai.Core.Models;
using Quai.Core.Security;
using Quai.Core.Serial;
using Quai.Core.Services;
using Quai.Core.Ssh;
using Quai.Core.Storage;
using Quai.Core.ViewModels;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using static Quai.App.NativeMethods;

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
    private const string ProbeSwitch = "--startup-probe";
    private const string UpdateSwitch = "--update-test";

    // Mesures du parcours (démarrage, mémoire) : écrites à la fin dans smoke-metrics.md.
    private static readonly SmokeMetrics Metrics = new();

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

    /// <summary>
    /// Mesure du démarrage seul (CI) : <c>Quai.exe --startup-probe &lt;dossier&gt;</c> ajoute la durée jusqu'au premier
    /// rendu à <c>startup-ms.txt</c> puis quitte. Lancé plusieurs fois de suite : premier lancement, puis lancements
    /// suivants (fichiers déjà en cache, déjà analysés par l'antivirus).
    /// </summary>
    public static bool TryGetStartupProbeDirectory(out string directory)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.FindIndex(args, a => string.Equals(a, ProbeSwitch, StringComparison.OrdinalIgnoreCase));
        directory = index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : string.Empty;
        return directory.Length > 0;
    }

    /// <summary>
    /// Mise à jour en place (CI) : <c>Quai.exe --update-test &lt;dossier&gt; &lt;installateur&gt;</c>. Quai, installé,
    /// se voit proposer cet installateur comme nouvelle version, le télécharge, le vérifie, le lance en mode silencieux
    /// et se ferme ; le workflow vérifie ensuite que l'installateur a réussi et a relancé Quai.
    /// </summary>
    public static bool TryGetUpdateTest(out string directory, out string installer)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.FindIndex(args, a => string.Equals(a, UpdateSwitch, StringComparison.OrdinalIgnoreCase));
        var found = index >= 0 && index + 2 < args.Length;
        directory = found ? Path.GetFullPath(args[index + 1]) : string.Empty;
        installer = found ? Path.GetFullPath(args[index + 2]) : string.Empty;
        return found;
    }

    public static async Task RunUpdateAsync(MainWindow window, IServiceProvider services, ILogger logger, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            await Task.Delay(1500);
            PaintBackground(window);

            var banner = services.GetRequiredService<UpdateBannerViewModel>();
            await banner.CheckAsync();
            if (!banner.IsOpen)
            {
                throw new InvalidOperationException("Le bandeau ne propose pas la nouvelle version.");
            }

            if (!banner.CanInstallInPlace)
            {
                throw new InvalidOperationException(
                    "Quai, lancé depuis son installation, ne propose pas la mise à jour en place (installation non reconnue ?).");
            }

            await Task.Delay(500);
            await CaptureAsync(window.Root, Path.Combine(directory, "update-available.png"));

            // Un clic : téléchargement (capturé en cours), vérification, installation silencieuse, fermeture de Quai.
            var update = banner.ActAsync();
            await WaitUntilAsync(
                () => Task.FromResult(!banner.IsDownloading || banner.Progress >= 25),
                TimeSpan.FromSeconds(60),
                "Le téléchargement de la mise à jour n'avance pas.");
            if (banner.IsDownloading)
            {
                await CaptureAsync(window.Root, Path.Combine(directory, "update-downloading.png"));
            }

            logger.LogInformation("Test de la mise à jour : installation lancée");
            await update;
            if (banner.HasFailed)
            {
                throw new InvalidOperationException($"Mise à jour en échec : {banner.Message}");
            }

            // Normalement, Quai est déjà en train de se fermer pour laisser la place à l'installateur.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test de la mise à jour en échec");
            Fail(directory, ex);
        }
    }

    public static void RecordStartup(string directory, TimeSpan elapsed)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "startup-ms.txt"), $"{(int)elapsed.TotalMilliseconds}{Environment.NewLine}");
        }
        catch (IOException)
        {
            Environment.Exit(1);
        }

        Environment.Exit(0);
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
            Metrics.Snapshot("fenêtre affichée");
            PaintBackground(window);

            foreach (var tag in MainWindow.PageTags)
            {
                logger.LogInformation("Test de fumée : page {Page}", tag);
                window.NavigateTo(tag);
                await Task.Delay(800);
                await CaptureAsync(window.Root, Path.Combine(directory, $"{tag}.png"));
                report.Add($"page {tag} : OK");
            }

            Metrics.Snapshot($"{MainWindow.PageTags.Count} pages ouvertes");

            await ConnectionBookAsync(window, services, directory, report);
            await TerminalAsync(window, services, directory, "terminal", report);
            report.Add("terminal (moteur Windows Terminal) : OK");
            await SerialTerminalAsync(window, services, directory, report);
            await LocalTerminalAsync(window, services, directory, report);
            await SftpAsync(window, services, directory, report);
            await SessionFeaturesAsync(window, services, directory, report);
            await SshDialogsAsync(services, directory, report);
            await FeedbackAsync(services, directory, report);
            await SettingsAsync(window, services, directory, report);
            await UpdateBannerAsync(window, services, directory, report);
            await CloseConfirmationAsync(window, services, directory, report);
            CheckVault(services, report);

            Metrics.Snapshot("fin du parcours (sessions fermées)");
            await File.WriteAllTextAsync(Path.Combine(directory, "smoke-metrics.md"), Metrics.ToMarkdown());
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
        repository.Save(SerialProfile());

        var viewModel = services.GetRequiredService<ConnectionsViewModel>();
        viewModel.Reload();
        window.NavigateTo(MainWindow.ConnectionsTag);
        await Task.Delay(800);
        await CaptureAsync(window.Root, Path.Combine(directory, "connections-filled.png"));
        if (viewModel.Connections.Count != 4 || viewModel.Filters.Count < 5)
        {
            throw new InvalidOperationException("Le carnet n'affiche pas les connexions d'exemple.");
        }

        if (viewModel.Connections.SingleOrDefault(c => c.IsSerial) is not { SupportsSftp: false, Endpoint: "COM3 · 115200 8N1" })
        {
            throw new InvalidOperationException("La connexion série n'apparaît pas correctement dans le carnet.");
        }

        report.Add("carnet rempli : OK");

        // Dossier replié : « Production/Web » disparaît du volet, « Production » garde son compteur.
        var production = viewModel.Filters.Single(f => f is { Kind: ConnectionFilterKind.Folder, Value: "Production" });
        if (!production.HasChildren)
        {
            throw new InvalidOperationException("Le dossier « Production » ne propose pas de se replier.");
        }

        viewModel.ToggleFolder(production);
        await Task.Delay(500);
        await CaptureAsync(window.Root, Path.Combine(directory, "connections-folded.png"));
        if (viewModel.Filters.Any(f => f.Value == "Production/Web") || production.Count != 2)
        {
            throw new InvalidOperationException("Replier « Production » ne cache pas son sous-dossier.");
        }

        viewModel.ToggleFolder(production);
        report.Add("dossiers repliables : OK");

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

        // Fiche d'une connexion série : ports détectés proposés, paramètres de la liaison.
        var serialEditor = new ConnectionEditorViewModel(
            services.GetRequiredService<IStrings>(),
            repository.GetAll().Single(p => p.IsSerial),
            viewModel.GetKnownFolders(),
            serialPorts: services.GetRequiredService<ISerialPortCatalog>());
        pending = dialogs.EditConnectionAsync(serialEditor);
        await Task.Delay(1200);
        dialog = dialogs.Current ?? throw new InvalidOperationException("La fiche de la connexion série ne s'est pas ouverte.");
        await CaptureAsync(dialog, Path.Combine(directory, "connection-editor-serial.png"));
        var shown = (dialog as Views.ConnectionEditorDialog)?.SerialTextForTest;
        if (shown is not ("COM3", "115200"))
        {
            throw new InvalidOperationException($"La fiche série n'affiche pas le port et la vitesse enregistrés : {shown}.");
        }

        dialog.Hide();
        await pending;
        if (!serialEditor.IsSerial || serialEditor.SerialPorts.Count != SmokeSessionFactory.SerialPorts.Count || serialEditor.SerialPortName != "COM3")
        {
            throw new InvalidOperationException("La fiche de la connexion série est incomplète (type, ports détectés ou port choisi).");
        }

        report.Add("fiche de connexion série : OK");
    }

    private static ConnectionProfile SerialProfile() => new()
    {
        Name = "Console du routeur",
        Protocol = ConnectionProtocol.Serial,
        Serial = new SerialSettings { PortName = "COM3", BaudRate = 115200 },
        Folder = "Atelier",
        Tags = ["console"],
    };

    /// <summary>
    /// Onglet relié à un port série de démonstration : bannière de l'appareil, saisie renvoyée en écho,
    /// et signal « break » (commande de l'onglet) auquel l'appareil répond.
    /// </summary>
    private static async Task SerialTerminalAsync(MainWindow window, IServiceProvider services, string directory, List<string> report)
    {
        var terminals = services.GetRequiredService<TerminalsViewModel>();
        services.GetRequiredService<ISessionLauncher>().OpenTerminal(SerialProfile());
        var tab = terminals.SelectedTab ?? throw new InvalidOperationException("Aucun onglet de terminal série n'a été créé.");
        await WaitUntilAsync(
            () => Task.FromResult(tab.IsConnected),
            TimeSpan.FromSeconds(60),
            "Le terminal série ne s'est pas ouvert.");

        var page = window.FindPage(MainWindow.TerminalsTag) as Views.TerminalsPage
            ?? throw new InvalidOperationException("Page des terminaux introuvable.");
        var view = page.SelectedTerminal ?? throw new InvalidOperationException("Vue du terminal série introuvable.");

        view.TypeForTest("show version\r");
        if (!tab.CanSendBreak || !tab.SendBreakCommand.CanExecute(null))
        {
            throw new InvalidOperationException("Le signal « break » n'est pas proposé sur l'onglet série.");
        }

        tab.SendBreakCommand.Execute(null);
        var screen = string.Empty;
        await WaitUntilAsync(
            () => Task.FromResult((screen = view.ReadScreenText()).Contains(SmokeSessionFactory.SerialBanner, StringComparison.Ordinal)
                && screen.Contains("show version", StringComparison.Ordinal)
                && screen.Contains(SmokeSessionFactory.BreakAnswer, StringComparison.Ordinal)),
            TimeSpan.FromSeconds(20),
            "La sortie du port série (bannière, écho, réponse au break) n'apparaît pas dans le terminal.");
        await File.WriteAllTextAsync(Path.Combine(directory, "terminal-serial-text.txt"), screen);

        await Task.Delay(500);
        await ScreenCapture.SaveAsync(WinRT.Interop.WindowNative.GetWindowHandle(window), Path.Combine(directory, "terminal-serial-tab.png"));

        await terminals.CloseAsync(tab);
        if (!terminals.IsEmpty)
        {
            throw new InvalidOperationException("L'onglet du terminal série ne s'est pas fermé.");
        }

        report.Add("terminal série (écho, signal break) : OK");
    }

    /// <summary>
    /// Vrai terminal local, ouvert comme par le bouton « Terminal local » : invite de commandes de la machine dans le
    /// ConPTY embarqué (OpenConsole.exe livré avec Quai, pas celui de Windows), commande exécutée, « exit » qui ferme
    /// la session et propose de relancer le shell.
    /// </summary>
    private static async Task LocalTerminalAsync(MainWindow window, IServiceProvider services, string directory, List<string> report)
    {
        var terminals = services.GetRequiredService<TerminalsViewModel>();
        window.NavigateTo(MainWindow.TerminalsTag);
        var page = window.FindPage(MainWindow.TerminalsTag) as Views.TerminalsPage
            ?? throw new InvalidOperationException("Page des terminaux introuvable.");
        if (!terminals.GetLocalShells().Any(s => s.Id == Quai.Core.Local.LocalShellCatalog.CommandPromptId))
        {
            throw new InvalidOperationException("L'invite de commandes n'est pas proposée comme shell local.");
        }

        // Shells administrateur (non lancés : l'UAC ne se valide pas en CI) : dossiers protégés, sans script de démarrage.
        var admins = terminals.GetAdminShells();
        var system = Environment.SystemDirectory;
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (admins.SingleOrDefault(s => s.Id == "admin:cmd") is not { Arguments: ["/D"] } adminCmd
            || !string.Equals(adminCmd.Path, Path.Combine(system, "cmd.exe"), StringComparison.OrdinalIgnoreCase)
            || admins.SingleOrDefault(s => s.Id == "admin:powershell") is not { Arguments: ["-NoLogo", "-NoProfile"] }
            || admins.Any(s => !s.Path.StartsWith(system + "\\", StringComparison.OrdinalIgnoreCase)
                && !s.Path.StartsWith(programFiles + "\\", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Shells administrateur inattendus : {string.Join(" ; ", admins.Select(s => s.Id + " = " + s.CommandLine))}");
        }

        page.OpenLocalTerminal(Quai.Core.Local.LocalShellCatalog.CommandPromptId);
        var tab = terminals.SelectedTab is { IsLocal: true } selected
            ? selected
            : throw new InvalidOperationException("Aucun onglet de terminal local n'a été créé.");
        await WaitUntilAsync(() => Task.FromResult(tab.IsConnected), TimeSpan.FromSeconds(60), "Le terminal local ne s'est pas ouvert.");
        var view = page.SelectedTerminal ?? throw new InvalidOperationException("Vue du terminal local introuvable.");

        // L'échappement « ^ » reste dans la commande affichée : seul le résultat contient le marqueur complet.
        view.TypeForTest("echo QUAI-LOCAL-^OK\r");
        var screen = string.Empty;
        await WaitUntilAsync(
            () => Task.FromResult((screen = view.ReadScreenText()).Contains("QUAI-LOCAL-OK", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(30),
            "La commande tapée dans l'invite de commandes locale n'a rien affiché.");
        await File.WriteAllTextAsync(Path.Combine(directory, "terminal-local-text.txt"), screen);

        var hosts = System.Diagnostics.Process.GetProcessesByName("OpenConsole")
            .Select(p =>
            {
                try
                {
                    return p.MainModule?.FileName;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    return null;
                }
            })
            .OfType<string>()
            .ToList();
        if (!hosts.Any(h => h.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Le ConPTY embarqué (OpenConsole.exe de Quai) n'est pas utilisé ; hôtes trouvés : {string.Join(", ", hosts)}");
        }

        await Task.Delay(500);
        await ScreenCapture.SaveAsync(WinRT.Interop.WindowNative.GetWindowHandle(window), Path.Combine(directory, "terminal-local-tab.png"));

        view.TypeForTest("exit\r");
        await WaitUntilAsync(() => Task.FromResult(tab.CanReconnect), TimeSpan.FromSeconds(20), "« exit » n'a pas fermé le terminal local.");
        if (tab.IsAutoReconnecting || tab.ReconnectText != Loc.Get("Terminal_Restart"))
        {
            throw new InvalidOperationException("Un terminal local fermé doit proposer « Relancer », sans reconnexion automatique.");
        }

        await tab.ReconnectCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => Task.FromResult(tab.IsConnected), TimeSpan.FromSeconds(30), "« Relancer » n'a pas rouvert le shell local.");

        await terminals.CloseAsync(tab);
        if (!terminals.IsEmpty)
        {
            throw new InvalidOperationException("L'onglet du terminal local ne s'est pas fermé.");
        }

        report.Add($"terminal local (invite de commandes réelle, ConPTY embarqué {Path.GetRelativePath(AppContext.BaseDirectory, hosts.First(h => h.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)))}, exit, relance) : OK");
    }

    /// <summary>
    /// Onglet de terminal relié à une session de démonstration : valide le chargement du moteur natif de
    /// Windows Terminal, la saisie au clavier, l'affichage, et l'effacement de sa fenêtre derrière un dialogue
    /// ou quand on change de page.
    /// </summary>
    private static async Task TerminalAsync(
        MainWindow window, IServiceProvider services, string directory, string captureName, List<string> report, bool switchBackground = false)
    {
        var terminals = services.GetRequiredService<TerminalsViewModel>();
        services.GetRequiredService<ISessionLauncher>().OpenTerminal(
            new ConnectionProfile { Name = "Démonstration", Host = "demo.example.org", Username = "quai" });
        var tab = terminals.SelectedTab ?? throw new InvalidOperationException("Aucun onglet de terminal n'a été créé.");

        // La connexion ne démarre qu'une fois le moteur créé, affiché et dimensionné.
        await WaitUntilAsync(
            () => Task.FromResult(tab.IsConnected),
            TimeSpan.FromSeconds(60),
            "Le terminal ne s'est pas initialisé (moteur Windows Terminal).");

        var page = window.FindPage(MainWindow.TerminalsTag) as Views.TerminalsPage
            ?? throw new InvalidOperationException("Page des terminaux introuvable.");
        var view = page.SelectedTerminal ?? throw new InvalidOperationException("Vue du terminal introuvable.");

        // Frappe comme au clavier : messages Windows -> moteur -> session -> écho -> moteur.
        view.TypeForTest("echo quai\r");
        var screen = string.Empty;
        await WaitUntilAsync(
            () => Task.FromResult((screen = view.ReadScreenText()).Contains(SmokeSessionFactory.Banner, StringComparison.Ordinal)
                && screen.Contains("echo quai", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(20),
            "La sortie de la session n'apparaît pas dans le terminal.");
        await File.WriteAllTextAsync(Path.Combine(directory, $"{captureName}-text.txt"), screen);
        if (captureName == "terminal")
        {
            Metrics.Snapshot("terminal SSH ouvert");
        }

        await Task.Delay(500);
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        await ScreenCapture.SaveAsync(windowHandle, Path.Combine(directory, $"{captureName}-tab.png"));
        var colors = await ScreenCapture.SaveAsync(view.TerminalWindowForTest, Path.Combine(directory, $"{captureName}.png"));
        report.Add($"rendu du terminal ({captureName}) : {colors} couleur(s) à l'écran");

        // Fond réellement dessiné par le moteur : celui du réglage, puis l'autre, appliqué à chaud.
        var settings = services.GetRequiredService<ISettingsService>();
        var fluent = settings.Current.TerminalFluentBackground;
        await ExpectTerminalBackgroundAsync(view, fluent);
        if (switchBackground)
        {
            settings.Update(s => s.TerminalFluentBackground = !fluent);
            await ExpectTerminalBackgroundAsync(view, !fluent);
            await ScreenCapture.SaveAsync(windowHandle, Path.Combine(directory, $"{captureName}-{(fluent ? "classic" : "fluent")}-tab.png"));
            settings.Update(s => s.TerminalFluentBackground = fluent);
            await ExpectTerminalBackgroundAsync(view, fluent);
            report.Add($"fond du terminal ({captureName}) : Fluent et classique appliqués à chaud : OK");
        }

        if (captureName == "terminal")
        {
            // Volet posé à côté du contenu (fenêtre en plein écran), replié puis redéplié : son animation fait glisser
            // le contenu sans relancer la mise en page, le terminal (fenêtre native) doit quand même suivre.
            // D'abord sans le suivi, pour constater que le test reproduit bien le bug (animations de Windows actives),
            // puis avec.
            var animations = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            TerminalAirspace.FollowAnimations = false;
            await window.FoldPaneBesideContentForTestAsync();
            var without = view.PlacementErrorForTest();
            await ScreenCapture.SaveAsync(windowHandle, Path.Combine(directory, "terminal-pane-reopened-without-fix.png"));
            window.RestorePaneForTest();
            TerminalAirspace.FollowAnimations = true;
            await Task.Delay(800);

            await window.FoldPaneBesideContentForTestAsync();
            var error = view.PlacementErrorForTest();
            await ScreenCapture.SaveAsync(windowHandle, Path.Combine(directory, "terminal-pane-reopened.png"));
            window.RestorePaneForTest();
            await Task.Delay(800);
            if (error is not (>= 0 and <= 2))
            {
                throw new InvalidOperationException(
                    $"Volet replié puis redéplié : le terminal n'est plus sur sa zone (écart : {error?.ToString() ?? "terminal masqué"} px).");
            }

            report.Add(
                $"terminal : suit le volet replié puis redéplié : OK (écart {error} px ; sans le suivi : {without?.ToString() ?? "masqué"} px ; "
                + $"animations de Windows {(animations ? "actives" : "désactivées")})");
        }

        // La fenêtre native passe derrière les dialogues…
        var dialogs = services.GetRequiredService<DialogService>();
        var pending = dialogs.ShowErrorAsync("Test de fumée", "Le terminal doit s'effacer derrière ce dialogue.");
        await WaitUntilAsync(
            () => Task.FromResult(dialogs.Current is not null && !view.IsTerminalShown),
            TimeSpan.FromSeconds(10),
            "Le terminal reste affiché par-dessus un dialogue.");
        await Task.Delay(600);
        await ScreenCapture.SaveAsync(windowHandle, Path.Combine(directory, $"{captureName}-dialog.png"));
        dialogs.Current?.Hide();
        await pending;
        await WaitUntilAsync(
            () => Task.FromResult(view.IsTerminalShown),
            TimeSpan.FromSeconds(10),
            "Le terminal ne réapparaît pas après la fermeture du dialogue.");

        // … et disparaît avec sa page.
        window.NavigateTo(MainWindow.ConnectionsTag);
        await WaitUntilAsync(
            () => Task.FromResult(!view.IsTerminalShown),
            TimeSpan.FromSeconds(10),
            "Le terminal reste affiché sur une autre page.");
        window.NavigateTo(MainWindow.TerminalsTag);
        await WaitUntilAsync(
            () => Task.FromResult(view.IsTerminalShown),
            TimeSpan.FromSeconds(10),
            "Le terminal ne réapparaît pas en revenant sur sa page.");

        await terminals.CloseAsync(tab);
        if (!terminals.IsEmpty)
        {
            throw new InvalidOperationException("L'onglet de terminal ne s'est pas fermé.");
        }
    }

    /// <summary>
    /// Explorateur SFTP relié à un serveur de démonstration en mémoire : double panneau, envoi d'un fichier
    /// avec progression, éditeur de droits et saisie d'un nom.
    /// </summary>
    private static async Task SftpAsync(MainWindow window, IServiceProvider services, string directory, List<string> report)
    {
        // Dossier local d'exemple, pour un panneau de gauche reproductible.
        var local = Path.Combine(directory, "data", "exemple");
        Directory.CreateDirectory(Path.Combine(local, "photos"));
        Directory.CreateDirectory(Path.Combine(local, "site"));
        File.WriteAllText(Path.Combine(local, "rapport.txt"), new string('r', 48_000));
        File.WriteAllText(Path.Combine(local, "todo.md"), "- tester Quai\n");

        var sessions = services.GetRequiredService<SftpSessionsViewModel>();
        services.GetRequiredService<ISessionLauncher>().OpenSftp(
            new ConnectionProfile { Name = "Démonstration", Host = "demo.example.org", Username = "quai" });
        var tab = sessions.SelectedTab ?? throw new InvalidOperationException("Aucun onglet SFTP n'a été créé.");
        await WaitUntilAsync(() => Task.FromResult(tab.IsConnected), TimeSpan.FromSeconds(20), "La session SFTP de démonstration ne s'est pas ouverte.");

        tab.NavigateLocal(local);
        if (tab.RemoteItems.Count < 6 || tab.LocalItems.Count < 4)
        {
            throw new InvalidOperationException("Les panneaux de l'explorateur SFTP sont incomplets.");
        }

        await tab.UploadAsync([Path.Combine(local, "rapport.txt")]);
        await Task.Delay(350);
        await CaptureAsync(window.Root, Path.Combine(directory, "sftp-transfer.png"));
        await WaitUntilAsync(
            () => Task.FromResult(tab.Transfers.Count == 1 && tab.Transfers[0].Status == TransferStatus.Completed),
            TimeSpan.FromSeconds(20),
            "Le transfert de démonstration ne s'est pas terminé.");
        await Task.Delay(400);
        await CaptureAsync(window.Root, Path.Combine(directory, "sftp-browser.png"));
        if (!tab.RemoteItems.Any(i => i.Name == "rapport.txt"))
        {
            throw new InvalidOperationException("Le fichier envoyé n'apparaît pas dans le panneau distant.");
        }

        report.Add("explorateur SFTP (double panneau, transfert) : OK");

        var dialogs = services.GetRequiredService<DialogService>();
        await ShowAndCaptureAsync(
            dialogs,
            () => dialogs.EditPermissionsAsync(new PermissionsEditorViewModel("deploy.sh", 0b111_101_000)),
            Path.Combine(directory, "sftp-permissions.png"));
        await ShowAndCaptureAsync(
            dialogs,
            () => dialogs.PromptTextAsync("Renommer", "Nom", "rapport.txt", "OK"),
            Path.Combine(directory, "sftp-rename.png"));
        report.Add("dialogues SFTP (droits, renommer) : OK");

        await sessions.CloseAsync(tab);
    }

    /// <summary>
    /// Connexion rapide, coupure puis reconnexion automatique, snippets (page, fiche, sélecteur, envoi), explorateur
    /// SFTP ouvert dans le dossier du shell, puis modification d'un fichier distant renvoyée à l'enregistrement.
    /// </summary>
    private static async Task SessionFeaturesAsync(MainWindow window, IServiceProvider services, string directory, List<string> report)
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var terminals = services.GetRequiredService<TerminalsViewModel>();
        var factory = services.GetRequiredService<ISshSessionFactory>() as SmokeSessionFactory
            ?? throw new InvalidOperationException("Le test de fumée doit utiliser les sessions de démonstration.");
        var dialogs = services.GetRequiredService<DialogService>();

        // Connexion rapide : onglet hors carnet, qui propose d'être ajouté au carnet.
        const string QuickAddress = "quai@rapide.example.org:2222";
        var connections = services.GetRequiredService<ConnectionsViewModel>();
        window.NavigateTo(MainWindow.ConnectionsTag);
        connections.QuickConnectText = QuickAddress;
        await Task.Delay(500);
        await CaptureAsync(window.Root, Path.Combine(directory, "quick-connect-field.png"));
        await connections.QuickConnectCommand.ExecuteAsync(null);
        var tab = terminals.SelectedTab ?? throw new InvalidOperationException("La connexion rapide n'a ouvert aucun onglet.");
        if (!tab.IsQuickConnect || tab.Endpoint != QuickAddress)
        {
            throw new InvalidOperationException($"La connexion rapide a ouvert « {tab.Endpoint} » (connexion rapide : {tab.IsQuickConnect}).");
        }

        await WaitUntilAsync(() => Task.FromResult(tab.IsConnected), TimeSpan.FromSeconds(60), "La connexion rapide ne s'est pas connectée.");
        var page = window.FindPage(MainWindow.TerminalsTag) as Views.TerminalsPage
            ?? throw new InvalidOperationException("Page des terminaux introuvable.");
        var view = page.SelectedTerminal ?? throw new InvalidOperationException("Vue du terminal introuvable.");
        await WaitUntilAsync(
            () => Task.FromResult(view.ReadScreenText().Contains(SmokeSessionFactory.Banner, StringComparison.Ordinal)),
            TimeSpan.FromSeconds(20),
            "La connexion rapide n'affiche pas la session.");
        if (!tab.ShowSaveOffer)
        {
            throw new InvalidOperationException("L'onglet de la connexion rapide ne propose pas de l'ajouter au carnet.");
        }

        await Task.Delay(600);
        await ScreenCapture.SaveAsync(windowHandle, Path.Combine(directory, "quick-connect-tab.png"));
        report.Add("connexion rapide (onglet hors carnet, « Ajouter au carnet… ») : OK");

        // Coupure simulée : compte à rebours, puis reconnexion automatique.
        factory.DropLastTerminal();
        await WaitUntilAsync(
            () => Task.FromResult(tab.IsAutoReconnecting),
            TimeSpan.FromSeconds(10),
            "Une coupure ne déclenche pas la reconnexion automatique.");
        await Task.Delay(500);
        await ScreenCapture.SaveAsync(windowHandle, Path.Combine(directory, "terminal-reconnecting.png"));
        await WaitUntilAsync(
            () => Task.FromResult(tab.IsConnected && !tab.IsAutoReconnecting),
            TimeSpan.FromSeconds(30),
            $"La reconnexion automatique n'a pas abouti ({tab.StatusText}).");
        report.Add("reconnexion automatique après une coupure : OK");

        // Snippets : page, fiche, puis envoi depuis le terminal par le sélecteur (Ctrl+Maj+P).
        var snippets = services.GetRequiredService<ISnippetRepository>();
        snippets.Save(new Snippet { Name = "Espace disque", Command = "df -h" });
        snippets.Save(new Snippet { Name = "Derniers journaux", Command = "journalctl -n 50 --no-pager\nuptime", Run = false });
        window.NavigateTo(MainWindow.SnippetsTag);
        await Task.Delay(800);
        await CaptureAsync(window.Root, Path.Combine(directory, "snippets.png"));
        if (services.GetRequiredService<SnippetsViewModel>().Items.Count != 2)
        {
            throw new InvalidOperationException("La page des snippets n'affiche pas les snippets enregistrés.");
        }

        await ShowAndCaptureAsync(
            dialogs,
            () => dialogs.EditSnippetAsync(new SnippetEditorViewModel(
                services.GetRequiredService<IStrings>(),
                snippets.GetAll()[0],
                services.GetRequiredService<IConnectionRepository>().GetAll())),
            Path.Combine(directory, "snippet-editor.png"));

        window.NavigateTo(MainWindow.TerminalsTag);
        await WaitUntilAsync(() => Task.FromResult(view.IsTerminalShown), TimeSpan.FromSeconds(10), "Le terminal ne réapparaît pas.");
        var running = view.RunSnippetAsync();
        await WaitUntilAsync(
            () => Task.FromResult(dialogs.Current is Views.SnippetPickerDialog),
            TimeSpan.FromSeconds(10),
            "Le choix d'un snippet ne s'ouvre pas.");
        await Task.Delay(900);
        var picker = (Views.SnippetPickerDialog)dialogs.Current!;
        await CaptureAsync(picker, Path.Combine(directory, "snippet-picker.png"));
        picker.ViewModel.Filter = "disque";
        if (picker.ViewModel.Selected?.Command != "df -h")
        {
            throw new InvalidOperationException("Le filtre du sélecteur ne retrouve pas le snippet.");
        }

        picker.SendForTest();
        await running;
        await WaitUntilAsync(
            () => Task.FromResult(view.ReadScreenText().Contains("df -h", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(20),
            "Le snippet envoyé n'apparaît pas dans le terminal.");
        report.Add("snippets (page, fiche, sélecteur, envoi dans le terminal) : OK");

        // Explorateur SFTP ouvert dans le dossier courant du shell.
        var sftp = services.GetRequiredService<SftpSessionsViewModel>();
        await page.OpenSftpHereAsync(tab);
        var browser = sftp.SelectedTab ?? throw new InvalidOperationException("Aucun explorateur SFTP ouvert depuis le terminal.");
        await WaitUntilAsync(
            () => Task.FromResult(browser.IsConnected && browser.RemotePath == SmokeSessionFactory.WorkingDirectory),
            TimeSpan.FromSeconds(20),
            $"L'explorateur ne s'est pas ouvert dans le dossier du shell ({browser.RemotePath}).");
        if (!browser.RemoteItems.Any(i => i.Name == "index.html"))
        {
            throw new InvalidOperationException("Le dossier du shell ouvert dans l'explorateur est vide.");
        }

        await Task.Delay(700);
        await CaptureAsync(window.Root, Path.Combine(directory, "sftp-here.png"));
        report.Add("SFTP ouvert dans le dossier courant du shell : OK");

        // Modifier un fichier distant : copie locale « ouverte », enregistrée, renvoyée sur le serveur.
        await browser.NavigateRemoteAsync("/home/quai");
        await browser.EditRemoteAsync(browser.RemoteItems.Single(i => i.Name == "notes.txt"));
        var local = services.GetRequiredService<SmokeFileLauncher>().Opened.LastOrDefault()
            ?? throw new InvalidOperationException("Le fichier distant n'a pas été ouvert pour modification.");
        await File.WriteAllTextAsync(local, "modifié par le test de fumée\n");
        await WaitUntilAsync(
            () => Task.FromResult(browser.Transfers.Any(t => t.Name == "notes.txt" && t.Status == TransferStatus.Completed)),
            TimeSpan.FromSeconds(20),
            "L'enregistrement du fichier modifié n'a pas été renvoyé sur le serveur.");
        var sent = browser.RemoteItems.Single(i => i.Name == "notes.txt").Size;
        if (sent != new FileInfo(local).Length || !browser.HasEdits)
        {
            throw new InvalidOperationException($"Le fichier renvoyé ne correspond pas à la copie modifiée ({sent} octets).");
        }

        await Task.Delay(600);
        await CaptureAsync(window.Root, Path.Combine(directory, "sftp-edit.png"));
        await sftp.CloseAsync(browser);
        if (File.Exists(local))
        {
            throw new InvalidOperationException("La copie locale n'est pas supprimée à la fermeture de l'explorateur.");
        }

        report.Add("modification d'un fichier distant (copie locale, renvoi à l'enregistrement, nettoyage) : OK");

        await terminals.CloseAsync(tab);
        if (!terminals.IsEmpty)
        {
            throw new InvalidOperationException("L'onglet de la connexion rapide ne s'est pas fermé.");
        }
    }

    /// <summary>Réglages : passage au thème sombre et changement de police, appliqués à chaud (terminal compris).</summary>
    private static async Task SettingsAsync(MainWindow window, IServiceProvider services, string directory, List<string> report)
    {
        var settings = services.GetRequiredService<ISettingsService>();
        window.NavigateTo(MainWindow.SettingsTag);
        await Task.Delay(500);

        settings.Update(s =>
        {
            s.Theme = AppTheme.Dark;
            s.TerminalFontFamily = "Consolas";
            s.TerminalFontSize = 17;
        });
        await WaitUntilAsync(
            () => Task.FromResult(window.Root.ActualTheme == ElementTheme.Dark),
            TimeSpan.FromSeconds(10),
            "Le thème sombre ne s'est pas appliqué.");
        PaintBackground(window);
        await Task.Delay(700);
        await CaptureAsync(window.Root, Path.Combine(directory, "settings-dark.png"));

        var reloaded = new JsonSettingsService(services.GetRequiredService<AppPaths>()).Current;
        if (reloaded.Theme != AppTheme.Dark || reloaded.TerminalFontSize != 17)
        {
            throw new InvalidOperationException("Les réglages n'ont pas été enregistrés.");
        }

        await TerminalAsync(window, services, directory, "terminal-dark", report, switchBackground: true);

        settings.Update(s =>
        {
            s.Theme = AppTheme.Light;
            s.TerminalFontFamily = AppSettings.DefaultTerminalFontFamily;
            s.TerminalFontSize = AppSettings.DefaultTerminalFontSize;
        });
        await WaitUntilAsync(
            () => Task.FromResult(window.Root.ActualTheme == ElementTheme.Light),
            TimeSpan.FromSeconds(10),
            "Le retour au thème clair ne s'est pas appliqué.");
        PaintBackground(window);
        report.Add("réglages (thème, police du terminal) : OK");
    }

    /// <summary>Bandeau de mise à jour : une release plus récente (fictive, sans réseau) l'affiche.</summary>
    private static async Task UpdateBannerAsync(MainWindow window, IServiceProvider services, string directory, List<string> report)
    {
        var banner = services.GetRequiredService<UpdateBannerViewModel>();
        await banner.CheckAsync();
        if (!banner.IsOpen || banner.ReleasePage?.AbsoluteUri != $"https://github.com/memton80/quai-ssh/releases/tag/{SmokeSessionFactory.ReleaseTag}")
        {
            throw new InvalidOperationException("Le bandeau de mise à jour ne s'affiche pas pour une version plus récente.");
        }

        window.NavigateTo(MainWindow.ConnectionsTag);
        await Task.Delay(700);
        await CaptureAsync(window.Root, Path.Combine(directory, "update-banner.png"));
        banner.IsOpen = false;
        await Task.Delay(300);
        report.Add("bandeau de mise à jour : OK");
    }

    /// <summary>
    /// Fermeture de la fenêtre avec des sessions ouvertes (vrai message WM_CLOSE) : la confirmation les liste ;
    /// « Annuler » garde tout ouvert ; la fermeture groupée déconnecte ensuite chaque session.
    /// </summary>
    private static async Task CloseConfirmationAsync(MainWindow window, IServiceProvider services, string directory, List<string> report)
    {
        var launcher = services.GetRequiredService<ISessionLauncher>();
        var profile = new ConnectionProfile { Name = "Démonstration", Host = "demo.example.org", Username = "quai" };
        var sftp = services.GetRequiredService<SftpSessionsViewModel>();
        var terminals = services.GetRequiredService<TerminalsViewModel>();
        var factory = services.GetRequiredService<ISshSessionFactory>() as SmokeSessionFactory
            ?? throw new InvalidOperationException("Le test de fumée doit utiliser les sessions de démonstration.");
        var liveBefore = factory.LiveSessions;

        launcher.OpenSftp(profile);
        var browser = sftp.SelectedTab ?? throw new InvalidOperationException("Aucun onglet SFTP n'a été créé.");
        await WaitUntilAsync(() => Task.FromResult(browser.IsConnected), TimeSpan.FromSeconds(20), "La session SFTP de démonstration ne s'est pas ouverte.");
        launcher.OpenTerminal(profile);
        var terminal = terminals.SelectedTab ?? throw new InvalidOperationException("Aucun onglet de terminal n'a été créé.");
        await WaitUntilAsync(() => Task.FromResult(terminal.IsConnected), TimeSpan.FromSeconds(60), "Le terminal de démonstration ne s'est pas connecté.");

        var dialogs = services.GetRequiredService<DialogService>();
        PostMessageW(WinRT.Interop.WindowNative.GetWindowHandle(window), WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        await WaitUntilAsync(() => Task.FromResult(dialogs.Current is not null), TimeSpan.FromSeconds(10), "Aucune confirmation à la fermeture de la fenêtre.");
        await Task.Delay(900);
        var dialog = dialogs.Current ?? throw new InvalidOperationException("La confirmation de fermeture s'est refermée toute seule.");
        var text = (dialog.Content as Microsoft.UI.Xaml.Controls.TextBlock)?.Text ?? string.Empty;
        if (!text.Contains("1 terminal", StringComparison.Ordinal) || !text.Contains("1 session SFTP", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"La confirmation ne liste pas les sessions ouvertes : « {text} ».");
        }

        await CaptureAsync(dialog, Path.Combine(directory, "close-confirmation.png"));
        dialog.Hide();
        await WaitUntilAsync(() => Task.FromResult(dialogs.Current is null), TimeSpan.FromSeconds(10), "La confirmation ne se ferme pas.");
        await Task.Delay(500);
        if (!terminal.IsConnected || !browser.IsConnected || factory.LiveSessions != liveBefore + 2)
        {
            throw new InvalidOperationException("« Annuler » à la fermeture a quand même coupé des sessions.");
        }

        var closed = await services.GetRequiredService<AppShutdown>().CloseAllAsync(TimeSpan.FromSeconds(10));
        if (!closed || !terminals.IsEmpty || !sftp.IsEmpty || factory.LiveSessions != liveBefore)
        {
            throw new InvalidOperationException(
                $"La fermeture groupée n'a pas tout fermé (délai tenu : {closed}, sessions encore ouvertes : {factory.LiveSessions - liveBefore}).");
        }

        report.Add("fermeture : confirmation, annulation, déconnexion de toutes les sessions : OK");
    }

    /// <summary>Fond opaque derrière le contenu : le Mica n'apparaît pas dans les captures.</summary>
    private static void PaintBackground(MainWindow window)
    {
        if (window.Root is Microsoft.UI.Xaml.Controls.Panel panel)
        {
            panel.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(window.Root.ActualTheme == ElementTheme.Dark
                ? Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20)
                : Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3));
        }
    }

    /// <summary>Dialogues de connexion : premier contact, empreinte modifiée, saisie du mot de passe.</summary>
    private static async Task SshDialogsAsync(IServiceProvider services, string directory, List<string> report)
    {
        var dialogs = services.GetRequiredService<DialogService>();
        var presented = new HostKeyInfo("demo.example.org", 22, "ssh-ed25519", [.. Enumerable.Range(1, 51).Select(i => (byte)i)]);
        var known = presented with { Key = [.. Enumerable.Range(60, 51).Select(i => (byte)i)] };
        var profile = new ConnectionProfile { Name = "Démonstration", Host = "demo.example.org", Username = "quai" };

        await ShowAndCaptureAsync(dialogs, () => dialogs.ConfirmUnknownHostAsync(presented), Path.Combine(directory, "hostkey-unknown.png"));
        await ShowAndCaptureAsync(dialogs, () => dialogs.ConfirmChangedHostAsync(presented, [known]), Path.Combine(directory, "hostkey-changed.png"));
        await ShowAndCaptureAsync(dialogs, () => dialogs.PromptSecretAsync(profile, SecretKind.Password, isRetry: true), Path.Combine(directory, "secret-prompt.png"));
        report.Add("dialogues SSH (clé d'hôte, mot de passe) : OK");
    }

    /// <summary>Formulaire « idée ou bug » (les deux variantes) et adresse GitHub construite ; aucun navigateur n'est ouvert.</summary>
    private static async Task FeedbackAsync(IServiceProvider services, string directory, List<string> report)
    {
        var dialogs = services.GetRequiredService<DialogService>();
        var strings = services.GetRequiredService<IStrings>();
        var logs = services.GetRequiredService<AppPaths>().LogsDirectory;
        var system = Core.Feedback.FeedbackIssue.DescribeSystem();

        var idea = new FeedbackViewModel(strings, system)
        {
            Title = "Ouvrir deux terminaux côte à côte",
            Description = "Pour surveiller deux serveurs en même temps.",
            AreaIndex = 1,
        };
        await ShowAndCaptureAsync(dialogs, () => dialogs.ShowFeedbackAsync(idea, logs), Path.Combine(directory, "feedback-idea.png"));

        var bug = new FeedbackViewModel(strings, system, Core.Feedback.FeedbackKind.Bug)
        {
            Title = "Le transfert s'arrête à 99 %",
            Description = "Le fichier reste incomplet sur le serveur.",
            Steps = "1. Ouvrir une session SFTP\r2. Envoyer un gros fichier",
        };
        await ShowAndCaptureAsync(dialogs, () => dialogs.ShowFeedbackAsync(bug, logs), Path.Combine(directory, "feedback-bug.png"));

        var address = bug.BuildLink().Address.OriginalString;
        if (!address.StartsWith("https://github.com/memton80/quai-ssh/issues/new?template=bug.yml&title=", StringComparison.Ordinal)
            || !address.Contains("&etapes=1.%20Ouvrir%20une%20session%20SFTP%0A2.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Adresse GitHub inattendue pour un bug : {address}");
        }

        report.Add("formulaire « idée ou bug » : OK");
    }

    private static async Task ShowAndCaptureAsync(DialogService dialogs, Func<Task> show, string path)
    {
        var pending = show();
        await WaitUntilAsync(() => Task.FromResult(dialogs.Current is not null), TimeSpan.FromSeconds(10), "Le dialogue ne s'est pas ouvert.");
        await Task.Delay(900);
        var dialog = dialogs.Current ?? throw new InvalidOperationException("Le dialogue s'est refermé tout seul.");
        await CaptureAsync(dialog, path);
        dialog.Hide();
        await pending;
    }

    /// <summary>Le fond affiché par le moteur (couleur dominante de sa fenêtre) est celui de la palette attendue.</summary>
    private static async Task ExpectTerminalBackgroundAsync(Views.TerminalView view, bool fluent)
    {
        var expected = TerminalPalette.Background(view.ActualTheme, fluent);
        Windows.UI.Color? seen = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!((seen = ScreenCapture.DominantColor(view.TerminalWindowForTest)) is { } color
            && Math.Abs(color.R - expected.R) <= 2 && Math.Abs(color.G - expected.G) <= 2 && Math.Abs(color.B - expected.B) <= 2))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"Fond du terminal ({(fluent ? "Fluent" : "classique")}) : {Hex(expected)} attendu, {(seen is { } s ? Hex(s) : "rien")} affiché.");
            }

            await Task.Delay(250);
        }

        static string Hex(Windows.UI.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string failure)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(failure);
            }

            await Task.Delay(250);
        }
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
