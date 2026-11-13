using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quai.Core.Localization;
using Quai.Core.Security;
using Quai.Core.Services;
using Quai.Core.Ssh;
using Quai.Core.Storage;
using Quai.Core.ViewModels;
using Serilog;

namespace Quai.App.Services;

/// <summary>Composition des services de l'application (DI + journalisation fichier).</summary>
internal static class AppServices
{
    /// <param name="smokeTest">Test de fumée : les sessions SSH sont remplacées par une session de démonstration locale.</param>
    public static ServiceProvider Build(bool smokeTest = false)
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        // Journal local uniquement (pas de télémétrie). Ne jamais y écrire de secret.
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "quai-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddLogging(builder => builder.AddSerilog(serilog, dispose: true));

        // Stockage et secrets
        services.AddSingleton<IConnectionRepository, JsonConnectionRepository>();
        services.AddSingleton<ISecretStore, PasswordVaultSecretStore>();

        // Interface
        services.AddSingleton<IStrings, ReswStrings>();
        services.AddSingleton<IUiDispatcher>(new UiDispatcher());
        services.AddSingleton<ShellNavigator>();
        services.AddSingleton<IShellNavigator>(provider => provider.GetRequiredService<ShellNavigator>());
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(provider => provider.GetRequiredService<DialogService>());
        services.AddSingleton<IHostKeyPrompt>(provider => provider.GetRequiredService<DialogService>());
        services.AddSingleton<ICredentialPrompt>(provider => provider.GetRequiredService<DialogService>());

        // SSH
        services.AddSingleton<IKnownHostsStore, KnownHostsStore>();
        services.AddSingleton<SshConnector>();
        if (smokeTest)
        {
            services.AddSingleton<ISshSessionFactory, SmokeSessionFactory>();
        }
        else
        {
            services.AddSingleton<ISshSessionFactory, SshSessionFactory>();
        }

        // View-models
        services.AddSingleton<TerminalsViewModel>();
        services.AddSingleton<ISessionLauncher, SessionLauncher>();
        services.AddSingleton<ConnectionsViewModel>();

        return services.BuildServiceProvider();
    }
}
