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

    // Tous les filtres (y compris les sous-dossiers de dossiers repliés) et les dossiers repliés (pour la session).
    private readonly List<ConnectionFilterItem> _allFilters = [];
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Filtres affichés dans le volet de gauche : « toutes », puis les dossiers (sauf ceux d'un dossier replié), puis les tags.</summary>
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
            _allFilters.Clear();
            _allFilters.Add(new ConnectionFilterItem(ConnectionFilterKind.All, string.Empty, _strings.Get("Connections_FilterAll"), all.Count));

            var folders = all
                .SelectMany(p => FolderPath.SelfAndAncestors(p.Folder))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            // Un dossier replié qui n'a plus de sous-dossier n'a plus rien à cacher.
            _collapsedFolders.RemoveWhere(collapsed => !folders.Any(f => IsInside(f, collapsed)));
            foreach (var folder in folders)
            {
                var label = folder[(folder.LastIndexOf(FolderPath.Separator) + 1)..];
                _allFilters.Add(new ConnectionFilterItem(
                    ConnectionFilterKind.Folder,
                    folder,
                    label,
                    all.Count(p => FolderPath.IsSameOrDescendant(p.Folder, folder)),
                    hasChildren: folders.Any(f => IsInside(f, folder)))
                {
                    IsExpanded = !_collapsedFolders.Contains(folder),
                });
            }

            var tags = all
                .SelectMany(p => p.Tags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase);
            foreach (var tag in tags)
            {
                _allFilters.Add(new ConnectionFilterItem(
                    ConnectionFilterKind.Tag,
                    tag,
                    tag,
                    all.Count(p => p.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))));
            }

            Filters.Clear();
            foreach (var item in _allFilters.Where(IsShown))
            {
                Filters.Add(item);
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

    /// <summary>Replie ou déplie un dossier : ses sous-dossiers disparaissent du volet, son compteur ne change pas.</summary>
    [RelayCommand]
    public void ToggleFolder(ConnectionFilterItem? folder)
    {
        if (folder is { Kind: ConnectionFilterKind.Folder, HasChildren: true })
        {
            SetExpanded(folder, !folder.IsExpanded);
        }
    }

    /// <summary>Flèche droite : déplie le dossier sélectionné.</summary>
    /// <returns><c>true</c> si la touche a servi.</returns>
    public bool ExpandSelected()
    {
        if (SelectedFilter is { Kind: ConnectionFilterKind.Folder, HasChildren: true, IsExpanded: false } folder)
        {
            SetExpanded(folder, true);
            return true;
        }

        return false;
    }

    /// <summary>Flèche gauche : replie le dossier sélectionné, ou remonte à son dossier parent (comme l'Explorateur).</summary>
    /// <returns><c>true</c> si la touche a servi.</returns>
    public bool CollapseSelected()
    {
        if (SelectedFilter is not { Kind: ConnectionFilterKind.Folder } folder)
        {
            return false;
        }

        if (folder is { HasChildren: true, IsExpanded: true })
        {
            SetExpanded(folder, false);
            return true;
        }

        var parent = Filters.FirstOrDefault(f => f.Kind == ConnectionFilterKind.Folder && f.Depth == folder.Depth - 1 && folder.IsInside(f.Value));
        if (parent is null)
        {
            return false;
        }

        SelectedFilter = parent;
        return true;
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

    private static bool IsInside(string folder, string ancestor) =>
        folder.Length > ancestor.Length
        && folder[ancestor.Length] == FolderPath.Separator
        && folder.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase);

    /// <summary>Affiché dans le volet : tout sauf les sous-dossiers d'un dossier replié.</summary>
    private bool IsShown(ConnectionFilterItem item) =>
        item.Kind != ConnectionFilterKind.Folder || !_collapsedFolders.Any(item.IsInside);

    private void SetExpanded(ConnectionFilterItem folder, bool expanded)
    {
        folder.IsExpanded = expanded;
        if (expanded)
        {
            _collapsedFolders.Remove(folder.Value);
        }
        else
        {
            _collapsedFolders.Add(folder.Value);

            // Le sous-dossier sélectionné va disparaître : la sélection remonte au dossier replié (son contenu reste listé).
            if (SelectedFilter is { } selected && selected.IsInside(folder.Value))
            {
                SelectedFilter = folder;
            }
        }

        // Mise à jour sur place (pas de Clear) : la liste garde sa position et le focus clavier.
        var shown = _allFilters.Where(IsShown).ToList();
        for (var i = Filters.Count - 1; i >= 0; i--)
        {
            if (!shown.Contains(Filters[i]))
            {
                Filters.RemoveAt(i);
            }
        }

        for (var i = 0; i < shown.Count; i++)
        {
            if (i >= Filters.Count || !ReferenceEquals(Filters[i], shown[i]))
            {
                Filters.Insert(i, shown[i]);
            }
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
