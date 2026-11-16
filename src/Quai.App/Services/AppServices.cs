using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quai.Core.Localization;
using Quai.Core.Security;
using Quai.Core.Serial;
using Quai.Core.Services;
using Quai.Core.Ssh;
using Quai.Core;
using Quai.Core.Storage;
using Quai.Core.Terminal;
using Quai.Core.Updates;
using Quai.Core.ViewModels;
using Serilog;

namespace Quai.App.Services;

/// <summary>Composition des services de l'application (DI + journalisation fichier).</summary>
internal static class AppServices
{
    /// <param name="smokeTest">Test de fumée : les sessions SSH et série sont remplacées par des sessions de démonstration locales.</param>
    /// <param name="updateTestInstaller">Test de la mise à jour (CI) : cet installateur local est présenté comme la dernière release.</param>
    public static ServiceProvider Build(bool smokeTest = false, string? updateTestInstaller = null)
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
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<ISecretStore, PasswordVaultSecretStore>();
        services.AddSingleton<ILocalRecycler, RecycleBin>();

        // Interface
        services.AddSingleton<IStrings, ReswStrings>();
        services.AddSingleton<IUiDispatcher>(new UiDispatcher());
        services.AddSingleton<ThemeService>();
        services.AddSingleton<ShellNavigator>();
        services.AddSingleton<IShellNavigator>(provider => provider.GetRequiredService<ShellNavigator>());
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(provider => provider.GetRequiredService<DialogService>());
        services.AddSingleton<IHostKeyPrompt>(provider => provider.GetRequiredService<DialogService>());
        services.AddSingleton<ICredentialPrompt>(provider => provider.GetRequiredService<DialogService>());

        // SSH et port série
        services.AddSingleton<IKnownHostsStore, KnownHostsStore>();
        services.AddSingleton<SshConnector>();
        if (smokeTest)
        {
            services.AddSingleton<SmokeSessionFactory>();
            services.AddSingleton<ISshSessionFactory>(provider => provider.GetRequiredService<SmokeSessionFactory>());
            services.AddSingleton<ISerialSessionFactory>(provider => provider.GetRequiredService<SmokeSessionFactory>());
            services.AddSingleton<ISerialPortCatalog>(provider => provider.GetRequiredService<SmokeSessionFactory>());
        }
        else
        {
            services.AddSingleton<ISshSessionFactory, SshSessionFactory>();
            services.AddSingleton<ISerialSessionFactory, SerialSessionFactory>();
            services.AddSingleton<ISerialPortCatalog, SerialPortCatalog>();
        }

        services.AddSingleton<ITerminalSessionFactory, TerminalSessionFactory>();

        // View-models
        services.AddSingleton<TerminalsViewModel>();
        services.AddSingleton<SftpSessionsViewModel>();
        services.AddSingleton<ISessionLauncher, SessionLauncher>();
        services.AddSingleton<ConnectionsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AppShutdown>();

        // Mises à jour : une requête à l'API publique de GitHub, puis, sur demande, le téléchargement de l'installateur
        // (les tests de fumée n'accèdent pas au réseau).
        var installerName = UpdateChecker.InstallerNameFor(RuntimeInformation.ProcessArchitecture);
        if (updateTestInstaller is not null)
        {
            var (source, downloader) = SmokeUpdate.Create(updateTestInstaller, installerName);
            services.AddSingleton(source);
            services.AddSingleton(downloader);
        }
        else
        {
            services.AddSingleton<IReleaseSource>(smokeTest ? new SmokeReleaseSource() : new GitHubReleaseSource());
            services.AddSingleton(_ => new UpdateDownloader());
        }

        services.AddSingleton(provider => new UpdateChecker(provider.GetRequiredService<IReleaseSource>(), AppInfo.Version, installerName));
        services.AddSingleton<IUpdateInstaller, UpdateInstaller>();
        services.AddSingleton<UpdateBannerViewModel>();

        return services.BuildServiceProvider();
    }
}
