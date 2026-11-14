using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Localization;
using Quai.Core.Storage;
using Quai.Core.Updates;

namespace Quai.Core.ViewModels;

/// <summary>Bandeau « nouvelle version disponible » en haut de la fenêtre.</summary>
public sealed partial class UpdateBannerViewModel : ObservableObject
{
    private readonly UpdateChecker _checker;
    private readonly ISettingsService _settings;
    private readonly IStrings _strings;
    private readonly ILogger _logger;

    public UpdateBannerViewModel(UpdateChecker checker, ISettingsService settings, IStrings strings, ILogger<UpdateBannerViewModel>? logger = null)
    {
        _checker = checker;
        _settings = settings;
        _strings = strings;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        Message = string.Empty;
    }

    /// <summary>Bandeau affiché ; l'utilisateur peut le fermer (il revient au prochain démarrage).</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; }

    /// <summary>Page de la release sur GitHub (téléchargements et notes de version).</summary>
    public Uri? ReleasePage { get; private set; }

    /// <summary>
    /// Interroge GitHub si le réglage l'autorise. Sans réseau, avec GitHub indisponible ou une réponse
    /// inattendue, rien ne s'affiche : seule une ligne est écrite dans le journal.
    /// </summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
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
            ReleasePage = update.ReleasePage;
            Message = _strings.Format("Update_Message", update.Version, AppInfo.Version);
            IsOpen = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Vérification des mises à jour impossible ({Error})", ex.GetType().Name);
        }
    }
}
