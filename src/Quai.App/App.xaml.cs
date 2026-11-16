using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Quai.App.Services;
using Quai.Core.ViewModels;

namespace Quai.App;

/// <summary>Point d'entrée de l'application : conteneur de services, fenêtre principale, erreurs non gérées.</summary>
public partial class App : Application
{
    private readonly ILogger<App> _logger;
    private readonly string? _smokeTestDirectory;
    private readonly string? _startupProbeDirectory;
    private readonly string? _updateTestDirectory;

    public App()
    {
        // Désinstallation : effacer les secrets du coffre puis quitter, sans créer de fenêtre ni de fichier.
        if (SecretPurge.IsRequested())
        {
            Environment.Exit(SecretPurge.Run());
        }

        if (SmokeTest.TryGetOutputDirectory(out var smokeDirectory))
        {
            _smokeTestDirectory = smokeDirectory;
            SmokeTest.IsolateData(smokeDirectory);
        }
        else if (SmokeTest.TryGetStartupProbeDirectory(out var probeDirectory))
        {
            _startupProbeDirectory = probeDirectory;
            SmokeTest.IsolateData(probeDirectory);
        }

        string? updateTestInstaller = null;
        if (SmokeTest.TryGetUpdateTest(out var updateDirectory, out var installer))
        {
            _updateTestDirectory = updateDirectory;
            updateTestInstaller = installer;
            SmokeTest.IsolateData(updateDirectory);
        }

        Services = AppServices.Build(smokeTest: _smokeTestDirectory is not null, updateTestInstaller);
        _logger = Services.GetRequiredService<ILogger<App>>();

        // Interface en français d'abord : force aussi les textes fournis par WinUI (boutons, menus).
        TrySetLanguage("fr-FR");

        InitializeComponent();
        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger.LogError(e.Exception, "Erreur non observée dans une tâche");
            e.SetObserved();
        };
    }

    public static new App Current => (App)Application.Current;

    public IServiceProvider Services { get; }

    public MainWindow? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _logger.LogInformation("Démarrage de Quai {Version}", typeof(App).Assembly.GetName().Version);

        MainWindow = new MainWindow();
        MainWindow.Activate();
        StartupClock.TrackFirstFrame(elapsed =>
        {
            _logger.LogInformation("Fenêtre affichée en {Milliseconds} ms", (int)elapsed.TotalMilliseconds);
            if (_startupProbeDirectory is not null)
            {
                SmokeTest.RecordStartup(_startupProbeDirectory, elapsed);
            }
        });

        if (_startupProbeDirectory is not null)
        {
            return;
        }

        if (_smokeTestDirectory is not null)
        {
            _ = SmokeTest.RunAsync(MainWindow, Services, _logger, _smokeTestDirectory);
        }
        else if (_updateTestDirectory is not null)
        {
            _ = SmokeTest.RunUpdateAsync(MainWindow, Services, _logger, _updateTestDirectory);
        }
        else
        {
            _ = CheckForUpdatesAsync();
        }
    }

    /// <summary>Vérification des mises à jour, un peu après le démarrage pour ne pas le ralentir.</summary>
    private async Task CheckForUpdatesAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        await Services.GetRequiredService<UpdateBannerViewModel>().CheckAsync();
    }

    private void TrySetLanguage(string language)
    {
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de forcer la langue {Language}", language);
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "Erreur non gérée");

        if ((_smokeTestDirectory ?? _updateTestDirectory) is { } testDirectory)
        {
            SmokeTest.Fail(testDirectory, e.Exception);
            return;
        }

        // Une erreur inattendue dans un gestionnaire d'interface ne doit pas fermer l'application
        // et couper les sessions SSH ouvertes : elle est journalisée et l'exécution continue.
        e.Handled = true;
    }
}
