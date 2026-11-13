using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Quai.App.Services;
using Quai.Core.Storage;

namespace Quai.App;

/// <summary>Point d'entrée de l'application : conteneur de services, fenêtre principale, erreurs non gérées.</summary>
public partial class App : Application
{
    private readonly ILogger<App> _logger;
    private string? _smokeTestDirectory;

    public App()
    {
        Services = AppServices.Build();
        _logger = Services.GetRequiredService<ILogger<App>>();

        // WebView2 : données utilisateur hors du dossier d'installation (qui peut être en lecture seule).
        var paths = Services.GetRequiredService<AppPaths>();
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", paths.WebViewDataDirectory);

        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public static new App Current => (App)Application.Current;

    public IServiceProvider Services { get; }

    public MainWindow? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _logger.LogInformation("Démarrage de Quai {Version}", typeof(App).Assembly.GetName().Version);

        MainWindow = new MainWindow();
        MainWindow.Activate();

        if (SmokeTest.TryGetOutputDirectory(out var directory))
        {
            _smokeTestDirectory = directory;
            _ = SmokeTest.RunAsync(MainWindow, _logger, directory);
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "Erreur non gérée");

        if (_smokeTestDirectory is not null)
        {
            SmokeTest.Fail(_smokeTestDirectory, e.Exception);
        }
    }
}
