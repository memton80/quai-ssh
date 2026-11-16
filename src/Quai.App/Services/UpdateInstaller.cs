using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Quai.App.Localization;
using Quai.Core.Services;
using Quai.Core.Storage;
using Quai.Core.Updates;

namespace Quai.App.Services;

/// <summary>
/// Installation d'une mise à jour téléchargée : l'installateur Inno Setup de la release est lancé en mode silencieux
/// (petite fenêtre de progression, aucune question), Quai se ferme, et l'installateur le relance une fois à jour
/// (<c>/RELAUNCH=1</c>, voir <c>installer/Quai.iss</c>). Installation par utilisateur : aucune élévation.
/// </summary>
internal sealed class UpdateInstaller : IUpdateInstaller
{
    // Entrée de désinstallation écrite par Inno Setup (AppId de installer/Quai.iss, à ne jamais changer).
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F6C1C0E-5B0E-4F0B-9E57-3C7F2A9D51A4}_is1";

    // Délai maximal pour déconnecter proprement les sessions avant l'installation.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly AppShutdown _shutdown;
    private readonly DialogService _dialogs;
    private readonly AppPaths _paths;
    private readonly ILogger _logger;
    private bool? _canInstall;

    public UpdateInstaller(AppShutdown shutdown, DialogService dialogs, AppPaths paths, ILogger<UpdateInstaller> logger)
    {
        _shutdown = shutdown;
        _dialogs = dialogs;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>Quai tourne depuis le dossier où l'installateur l'a mis (sinon : copie portable ou développement).</summary>
    public bool CanInstall => _canInstall ??= IsRunningFromInstallation();

    public async Task<bool> InstallAndRestartAsync(string installerPath)
    {
        var open = _shutdown.Inventory();
        if (!open.IsEmpty)
        {
            var message = string.Join(Environment.NewLine + Environment.NewLine, _shutdown.Describe(open), Loc.Get("Update_ConfirmRestart"));
            if (!await _dialogs.ConfirmAsync(Loc.Get("Update_ConfirmTitle"), message, Loc.Get("Update_ConfirmButton"), isDestructive: open.ActiveTransfers > 0))
            {
                return false;
            }

            if (!await _shutdown.CloseAllAsync(ShutdownTimeout))
            {
                _logger.LogWarning("Des sessions ne se sont pas fermées dans le délai ; mise à jour quand même");
            }
        }

        var start = new ProcessStartInfo(installerPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? string.Empty,
        };

        // /SILENT : seulement la fenêtre de progression ; /CLOSEAPPLICATIONS : ferme Quai s'il n'a pas encore fini
        // de quitter ; /RELAUNCH=1 : relance Quai une fois installé ; journal à côté de ceux de Quai.
        foreach (var argument in new[]
        {
            "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/RELAUNCH=1",
            $"/LOG={Path.Combine(_paths.LogsDirectory, "update-install.log")}",
        })
        {
            start.ArgumentList.Add(argument);
        }

        using (Process.Start(start) ?? throw new InvalidOperationException("L'installateur ne s'est pas lancé."))
        {
        }

        _logger.LogInformation("Installateur de la mise à jour lancé ({File}) ; fermeture de Quai", Path.GetFileName(installerPath));
        App.Current.MainWindow?.CloseForUpdate();
        return true;
    }

    public void OpenReleasePage(Uri page)
    {
        try
        {
            // Navigateur par défaut ; l'adresse est construite par l'application (page de release GitHub).
            Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Ouverture de la page de mise à jour impossible ({Error})", ex.GetType().Name);
        }
    }

    private bool IsRunningFromInstallation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key?.GetValue("InstallLocation") is not string location || string.IsNullOrWhiteSpace(location))
            {
                return false;
            }

            var installed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location));
            var running = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            var same = string.Equals(installed, running, StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("Mise à jour en place {State}", same ? "possible" : "impossible (copie portable)");
            return same;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            _logger.LogInformation("Installation introuvable dans le registre ({Error})", ex.GetType().Name);
            return false;
        }
    }
}
