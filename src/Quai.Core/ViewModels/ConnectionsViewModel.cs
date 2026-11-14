using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Localization;
using Quai.Core.Models;
using Quai.Core.Security;
using Quai.Core.Serial;
using Quai.Core.Services;
using Quai.Core.Storage;

namespace Quai.Core.ViewModels;

/// <summary>Carnet de connexions : filtres (dossiers, tags), recherche, création / modification / suppression.</summary>
public sealed partial class ConnectionsViewModel : ObservableObject
{
    private readonly IConnectionRepository _repository;
    private readonly ISecretStore _secrets;
    private readonly IDialogService _dialogs;
    private readonly IStrings _strings;
    private readonly ISessionLauncher? _launcher;
    private readonly ISerialPortCatalog? _serialPorts;
    private readonly ILogger _logger;
    private bool _reloading;

    public ConnectionsViewModel(
        IConnectionRepository repository,
        ISecretStore secrets,
        IDialogService dialogs,
        IStrings strings,
        ISessionLauncher? launcher = null,
        ILogger<ConnectionsViewModel>? logger = null,
        ISerialPortCatalog? serialPorts = null)
    {
        _repository = repository;
        _secrets = secrets;
        _dialogs = dialogs;
        _strings = strings;
        _launcher = launcher;
        _serialPorts = serialPorts;
        _logger = logger ?? NullLogger<ConnectionsViewModel>.Instance;
        SearchText = string.Empty;
        Reload();
    }

    /// <summary>Filtres du volet de gauche : « toutes », puis les dossiers, puis les tags.</summary>
    public ObservableCollection<ConnectionFilterItem> Filters { get; } = [];

    /// <summary>Connexions correspondant au filtre et à la recherche.</summary>
    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = [];

    [ObservableProperty]
    public partial ConnectionFilterItem? SelectedFilter { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    /// <summary>Le carnet ne contient aucune connexion (état vide initial).</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>Le carnet n'est pas vide mais rien ne correspond au filtre / à la recherche.</summary>
    [ObservableProperty]
    public partial bool HasNoResults { get; set; }

    /// <summary>Recharge filtres et liste depuis le stockage.</summary>
    public void Reload()
    {
        var all = _repository.GetAll();
        var previous = SelectedFilter;

        _reloading = true;
        try
        {
            Filters.Clear();
            Filters.Add(new ConnectionFilterItem(ConnectionFilterKind.All, string.Empty, _strings.Get("Connections_FilterAll"), all.Count));

            var folders = all
                .SelectMany(p => FolderPath.SelfAndAncestors(p.Folder))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase);
            foreach (var folder in folders)
            {
                var label = folder[(folder.LastIndexOf(FolderPath.Separator) + 1)..];
                Filters.Add(new ConnectionFilterItem(
                    ConnectionFilterKind.Folder,
                    folder,
                    label,
                    all.Count(p => FolderPath.IsSameOrDescendant(p.Folder, folder))));
            }

            var tags = all
                .SelectMany(p => p.Tags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase);
            foreach (var tag in tags)
            {
                Filters.Add(new ConnectionFilterItem(
                    ConnectionFilterKind.Tag,
                    tag,
                    tag,
                    all.Count(p => p.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))));
            }

            SelectedFilter = Filters.FirstOrDefault(f => f.IsSameFilter(previous)) ?? Filters[0];
        }
        finally
        {
            _reloading = false;
        }

        ApplyFilter(all);
    }

    /// <summary>Dossiers déjà utilisés (suggestions de la fiche de connexion).</summary>
    public IReadOnlyList<string> GetKnownFolders() =>
        [.. _repository.GetAll()
            .SelectMany(p => FolderPath.SelfAndAncestors(p.Folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)];

    [RelayCommand]
    public async Task NewAsync()
    {
        var folder = SelectedFilter is { Kind: ConnectionFilterKind.Folder } filter ? filter.Value : string.Empty;
        var editor = new ConnectionEditorViewModel(_strings, null, GetKnownFolders(), serialPorts: _serialPorts) { Folder = folder };
        if (await _dialogs.EditConnectionAsync(editor))
        {
            await SaveAsync(editor);
        }
    }

    public async Task EditAsync(ConnectionItemViewModel? item)
    {
        var profile = item is null ? null : _repository.Find(item.Id);
        if (profile is null)
        {
            return;
        }

        var editor = new ConnectionEditorViewModel(
            _strings,
            profile,
            GetKnownFolders(),
            hasStoredPassword: HasSecret(SecretKeys.Password(profile.Id)),
            hasStoredPassphrase: HasSecret(SecretKeys.Passphrase(profile.Id)),
            serialPorts: _serialPorts);

        if (await _dialogs.EditConnectionAsync(editor))
        {
            await SaveAsync(editor);
        }
    }

    /// <summary>Ouvre un terminal (SSH ou série) sur la connexion, dans sa version enregistrée la plus récente.</summary>
    public void OpenTerminal(ConnectionItemViewModel? item)
    {
        var profile = item is null ? null : _repository.Find(item.Id);
        if (profile is not null)
        {
            _launcher?.OpenTerminal(profile);
        }
    }

    /// <summary>Ouvre l'explorateur de fichiers SFTP sur la connexion (SSH uniquement).</summary>
    public void OpenSftp(ConnectionItemViewModel? item)
    {
        var profile = item is null ? null : _repository.Find(item.Id);
        if (profile is { IsSerial: false })
        {
            _launcher?.OpenSftp(profile);
        }
    }

    public void Duplicate(ConnectionItemViewModel? item)
    {
        var source = item is null ? null : _repository.Find(item.Id);
        if (source is null)
        {
            return;
        }

        var copy = source.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = _strings.Format("Connections_CopyName", source.DisplayName);
        copy.CreatedAt = DateTimeOffset.UtcNow;
        copy.LastConnectedAt = null;
        _repository.Save(copy);

        try
        {
            CopySecret(SecretKeys.Password(source.Id), SecretKeys.Password(copy.Id));
            CopySecret(SecretKeys.Passphrase(source.Id), SecretKeys.Passphrase(copy.Id));
        }
        catch (Exception ex)
        {
            // Le message d'une erreur de coffre ne contient pas le secret ; on ne journalise que le type.
            _logger.LogWarning("Copie des secrets impossible ({Error})", ex.GetType().Name);
        }

        Reload();
    }

    public async Task DeleteAsync(ConnectionItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            _strings.Get("Connections_DeleteTitle"),
            _strings.Format("Connections_DeleteMessage", item.Name),
            _strings.Get("Common_Delete"),
            isDestructive: true);
        if (!confirmed)
        {
            return;
        }

        _repository.Delete(item.Id);
        try
        {
            _secrets.Remove(SecretKeys.Password(item.Id));
            _secrets.Remove(SecretKeys.Passphrase(item.Id));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Suppression des secrets impossible ({Error})", ex.GetType().Name);
        }

        Reload();
    }

    partial void OnSelectedFilterChanged(ConnectionFilterItem? value)
    {
        if (!_reloading)
        {
            ApplyFilter(_repository.GetAll());
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_reloading)
        {
            ApplyFilter(_repository.GetAll());
        }
    }

    private async Task SaveAsync(ConnectionEditorViewModel editor)
    {
        var profile = editor.BuildProfile();
        _repository.Save(profile);

        try
        {
            // Une connexion série n'a aucun secret : ceux d'une ancienne configuration SSH sont effacés.
            var ssh = profile.Protocol == ConnectionProtocol.Ssh;
            StoreSecret(
                SecretKeys.Password(profile.Id),
                ssh && profile.Authentication == AuthenticationKind.Password && editor.SavePassword,
                editor.Password);
            StoreSecret(
                SecretKeys.Passphrase(profile.Id),
                ssh && profile.Authentication == AuthenticationKind.PrivateKey && editor.SavePassphrase,
                editor.Passphrase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Enregistrement dans le coffre impossible ({Error})", ex.GetType().Name);
            await _dialogs.ShowErrorAsync(_strings.Get("Vault_ErrorTitle"), _strings.Get("Vault_ErrorMessage"));
        }

        Reload();
    }

    /// <summary>
    /// <paramref name="keep"/> faux : on efface le secret. Vrai : on le remplace si une nouvelle valeur est saisie,
    /// sinon on conserve celui du coffre.
    /// </summary>
    private void StoreSecret(string key, bool keep, string? value)
    {
        if (!keep)
        {
            _secrets.Remove(key);
        }
        else if (!string.IsNullOrEmpty(value))
        {
            _secrets.Set(key, value);
        }
    }

    private void CopySecret(string from, string to)
    {
        var secret = _secrets.Get(from);
        if (!string.IsNullOrEmpty(secret))
        {
            _secrets.Set(to, secret);
        }
    }

    private bool HasSecret(string key)
    {
        try
        {
            return !string.IsNullOrEmpty(_secrets.Get(key));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Lecture du coffre impossible ({Error})", ex.GetType().Name);
            return false;
        }
    }

    private void ApplyFilter(IReadOnlyList<ConnectionProfile> all)
    {
        var filter = SelectedFilter;
        var matches = ConnectionFilter.Apply(
            all,
            SearchText,
            folder: filter is { Kind: ConnectionFilterKind.Folder } ? filter.Value : null,
            tag: filter is { Kind: ConnectionFilterKind.Tag } ? filter.Value : null);

        Connections.Clear();
        foreach (var profile in matches)
        {
            Connections.Add(new ConnectionItemViewModel(profile, this));
        }

        IsEmpty = all.Count == 0;
        HasNoResults = all.Count > 0 && Connections.Count == 0;
    }
}
