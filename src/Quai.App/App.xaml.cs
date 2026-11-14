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

        Services = AppServices.Build(smokeTest: _smokeTestDirectory is not null);
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

        if (_smokeTestDirectory is not null)
        {
            _ = SmokeTest.RunAsync(MainWindow, Services, _logger, _smokeTestDirectory);
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

        if (_smokeTestDirectory is not null)
        {
            SmokeTest.Fail(_smokeTestDirectory, e.Exception);
            return;
        }

        // Une erreur inattendue dans un gestionnaire d'interface ne doit pas fermer l'application
        // et couper les sessions SSH ouvertes : elle est journalisée et l'exécution continue.
        e.Handled = true;
    }
}
