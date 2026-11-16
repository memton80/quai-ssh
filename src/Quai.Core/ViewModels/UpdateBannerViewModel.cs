using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Localization;
using Quai.Core.Storage;
using Quai.Core.Updates;

namespace Quai.Core.ViewModels;

/// <summary>
/// Bandeau « nouvelle version disponible » en haut de la fenêtre. Un clic sur « Mettre à jour » télécharge
/// l'installateur, vérifie son empreinte, puis Quai se ferme et l'installateur le relance dans sa nouvelle version,
/// comme un navigateur. Copie portable (zip) : le bouton ouvre la page de la release.
/// </summary>
public sealed partial class UpdateBannerViewModel : ObservableObject
{
    private readonly UpdateChecker _checker;
    private readonly UpdateDownloader _downloader;
    private readonly IUpdateInstaller _installer;
    private readonly ISettingsService _settings;
    private readonly IStrings _strings;
    private readonly string _downloadDirectory;
    private readonly ILogger _logger;
    private AvailableUpdate? _update;
    private string? _downloaded;
    private CancellationTokenSource? _download;
    private bool _busy;

    public UpdateBannerViewModel(
        UpdateChecker checker,
        UpdateDownloader downloader,
        IUpdateInstaller installer,
        ISettingsService settings,
        IStrings strings,
        AppPaths paths,
        ILogger<UpdateBannerViewModel>? logger = null)
    {
        _checker = checker;
        _downloader = downloader;
        _installer = installer;
        _settings = settings;
        _strings = strings;
        _downloadDirectory = paths.UpdatesDirectory;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        Message = string.Empty;
        ActionText = string.Empty;
        IsActionEnabled = true;
    }

    /// <summary>Bandeau affiché ; le fermer annule un téléchargement en cours (il revient au prochain démarrage).</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; }

    /// <summary>« Mettre à jour », « Annuler » pendant le téléchargement, « Réessayer » après un échec.</summary>
    [ObservableProperty]
    public partial string ActionText { get; set; }

    /// <summary>Faux pendant l'installation : Quai est en train de se fermer.</summary>
    [ObservableProperty]
    public partial bool IsActionEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    /// <summary>Avancement du téléchargement, de 0 à 100.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial bool HasFailed { get; set; }

    /// <summary>Page de la release sur GitHub (notes de version, téléchargements).</summary>
    public Uri? ReleasePage => _update?.ReleasePage;

    /// <summary>Le bouton installe la mise à jour lui-même (sinon il ouvre la page de la release).</summary>
    public bool CanInstallInPlace => _update?.Installer is not null && _installer.CanInstall;

    /// <summary>
    /// Interroge GitHub si le réglage l'autorise. Sans réseau, avec GitHub indisponible ou une réponse
    /// inattendue, rien ne s'affiche : seule une ligne est écrite dans le journal.
    /// </summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        // Installateur de la mise à jour précédente : il a servi.
        UpdateDownloader.CleanUp(_downloadDirectory);

        if (!_settings.Current.CheckForUpdates)
        {
            return;
        }

        try
        {
            var update = await _checker.CheckAsync(cancellationToken);
            if (update is null)
            {
                _logger.LogInformation("Quai {Version} est à jour", AppInfo.Version);
                return;
            }

            _logger.LogInformation("Nouvelle version disponible : {Version}", update.Version);
            _update = update;
            ShowAvailable();
            IsOpen = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Vérification des mises à jour impossible ({Error})", ex.GetType().Name);
        }
    }

    /// <summary>Bouton du bandeau : annule le téléchargement en cours, sinon lance la mise à jour.</summary>
    public Task ActAsync()
    {
        if (IsDownloading)
        {
            CancelDownload();
            return Task.CompletedTask;
        }

        return UpdateAsync();
    }

    /// <summary>Télécharge et vérifie l'installateur (une seule fois), puis le lance : Quai se ferme et redémarre à jour.</summary>
    public async Task UpdateAsync()
    {
        if (_update is not { } update || _busy)
        {
            return;
        }

        if (!CanInstallInPlace || update.Installer is not { } package)
        {
            _installer.OpenReleasePage(update.ReleasePage);
            return;
        }

        _busy = true;
        HasFailed = false;
        var installing = false;
        CancellationTokenSource? cancellation = null;
        try
        {
            if (_downloaded is null || !File.Exists(_downloaded))
            {
                cancellation = new CancellationTokenSource();
                _download = cancellation;
                Progress = 0;
                IsDownloading = true;
                ActionText = _strings.Get("Update_Cancel");
                Message = _strings.Format("Update_Downloading", update.Version);

                // Créé ici, sur le fil d'interface : la progression y revient.
                var progress = new Progress<double>(value =>
                {
                    if (IsDownloading)
                    {
                        Progress = Math.Round(value * 100);
                    }
                });
                _downloaded = await _downloader.DownloadAsync(package, _downloadDirectory, progress, cancellation.Token);
                IsDownloading = false;
                Progress = 100;
                _logger.LogInformation("Mise à jour {Version} téléchargée, empreinte vérifiée", update.Version);
            }

            installing = true;
            IsActionEnabled = false;
            Message = _strings.Format("Update_Installing", update.Version);
            if (await _installer.InstallAndRestartAsync(_downloaded))
            {
                // Quai se ferme ; l'installateur prend la suite et relance la nouvelle version.
                return;
            }

            // Sessions gardées ouvertes : la mise à jour attend, déjà téléchargée.
            Message = _strings.Format("Update_Ready", update.Version);
            ActionText = _strings.Get("Update_Install");
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
            _logger.LogInformation("Téléchargement de la mise à jour {Version} annulé", update.Version);
            ShowAvailable();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Mise à jour {Version} impossible ({Error} : {Detail})", update.Version, ex.GetType().Name, ex.Message);
            var reason = installing ? "Update_FailedInstall"
                : ex is UpdateIntegrityException ? "Update_FailedIntegrity"
                : "Update_FailedDownload";
            HasFailed = true;
            Message = _strings.Format("Update_Failed", update.Version, _strings.Get(reason));
            ActionText = _strings.Get("Update_Retry");
        }
        finally
        {
            IsDownloading = false;
            IsActionEnabled = true;
            _download = null;
            cancellation?.Dispose();
            _busy = false;
        }
    }

    public void CancelDownload() => _download?.Cancel();

    partial void OnIsOpenChanged(bool value)
    {
        if (!value)
        {
            CancelDownload();
        }
    }

    private void ShowAvailable()
    {
        if (_update is not { } update)
        {
            return;
        }

        HasFailed = false;
        Progress = 0;
        Message = _strings.Format("Update_Message", update.Version, AppInfo.Version);
        ActionText = _strings.Get(CanInstallInPlace ? "Update_Install" : "Update_Download");
    }
}
