using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Localization;
using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Sftp;
using Quai.Core.Ssh;
using Renci.SshNet.Common;

namespace Quai.Core.ViewModels;

public enum SftpStatus
{
    Idle,
    Connecting,
    Connected,
    Disconnected,
    Failed,
}

/// <summary>
/// Explorateur SFTP à double panneau : dossier local à gauche, dossier distant à droite,
/// file de transferts avec progression et annulation.
/// </summary>
public sealed partial class SftpBrowserViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan TransferCleanupTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Au-delà, ouvrir un fichier dans un éditeur demande confirmation (téléchargement complet).</summary>
    public const long LargeEditSize = 50L * 1024 * 1024;

    private readonly ISshSessionFactory _factory;
    private readonly IDialogService _dialogs;
    private readonly IStrings _strings;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILocalRecycler? _recycler;
    private readonly ILogger _logger;
    private readonly FileSizeFormatter _sizes;
    private readonly IFileLauncher? _launcher;
    private readonly RemoteEditFiles? _editFiles;
    private readonly List<RemoteEdit> _edits = [];
    private ISftpSession? _session;
    private SftpTransferEngine? _engine;
    private CancellationTokenSource? _connectCancellation;
    private bool _pumping;
    private Task _pump = Task.CompletedTask;
    private bool _disposed;

    public SftpBrowserViewModel(
        ConnectionProfile profile,
        ISshSessionFactory factory,
        IDialogService dialogs,
        IStrings strings,
        IUiDispatcher dispatcher,
        ILocalRecycler? recycler = null,
        ILogger? logger = null,
        string? initialLocalPath = null,
        string? initialRemotePath = null,
        IFileLauncher? launcher = null,
        RemoteEditFiles? editFiles = null)
    {
        Profile = profile;
        _factory = factory;
        _dialogs = dialogs;
        _strings = strings;
        _dispatcher = dispatcher;
        _recycler = recycler;
        _logger = logger ?? NullLogger.Instance;
        _sizes = new FileSizeFormatter(strings);
        _launcher = launcher;
        _editFiles = editFiles;

        Status = SftpStatus.Idle;
        StatusText = string.Empty;
        EditsText = string.Empty;
        LocalPath = initialLocalPath ?? LocalFileSystem.HomeDirectory;
        // Dossier distant demandé (ouverture depuis un terminal) ; s'il est illisible, la connexion se rabat sur le dossier personnel.
        RemotePath = initialRemotePath ?? string.Empty;
        LoadLocal(LocalPath, reportErrors: false);
    }

    public ConnectionProfile Profile { get; }

    public string Title => Profile.DisplayName;

    public string Endpoint => Profile.Endpoint;

    public ObservableCollection<FileItemViewModel> LocalItems { get; } = [];

    public ObservableCollection<FileItemViewModel> RemoteItems { get; } = [];

    public ObservableCollection<TransferItemViewModel> Transfers { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(CanReconnect))]
    public partial SftpStatus Status { get; set; }

    /// <summary>Message affiché quand la session n'est pas (ou plus) connectée.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; }

    /// <summary>Dossier local affiché (chaîne vide = liste des lecteurs).</summary>
    [ObservableProperty]
    public partial string LocalPath { get; set; }

    [ObservableProperty]
    public partial string RemotePath { get; set; }

    [ObservableProperty]
    public partial bool IsRemoteLoading { get; set; }

    [ObservableProperty]
    public partial bool HasTransfers { get; set; }

    /// <summary>Des fichiers distants sont ouverts dans un éditeur : leurs enregistrements sont renvoyés.</summary>
    [ObservableProperty]
    public partial bool HasEdits { get; set; }

    /// <summary>« notes.txt est ouvert pour modification… » (bandeau de l'explorateur).</summary>
    [ObservableProperty]
    public partial string EditsText { get; set; }

    /// <summary>« Modifier » est possible (fichier distant, application capable d'ouvrir un éditeur).</summary>
    public bool CanEdit => _launcher is not null && _editFiles is not null;

    public bool IsConnected => Status == SftpStatus.Connected;

    public bool IsConnecting => Status == SftpStatus.Connecting;

    public bool CanReconnect => Status is SftpStatus.Disconnected or SftpStatus.Failed;

    // ---- Connexion ---------------------------------------------------------------------------

    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (_disposed || Status is SftpStatus.Connecting or SftpStatus.Connected)
        {
            return;
        }

        Status = SftpStatus.Connecting;
        StatusText = _strings.Format("Sftp_Connecting", Endpoint);

        var cancellation = new CancellationTokenSource();
        _connectCancellation = cancellation;
        try
        {
            var session = await _factory.OpenSftpAsync(Profile, cancellation.Token);
            if (_disposed)
            {
                await session.DisposeAsync();
                return;
            }

            _session = session;
            _engine = new SftpTransferEngine(session);
            session.Closed += OnSessionClosed;
            Status = SftpStatus.Connected;
            StatusText = string.Empty;

            var start = string.IsNullOrEmpty(RemotePath) ? session.HomeDirectory : RemotePath;
            if (!await LoadRemoteAsync(start, reportErrors: false) && start != session.HomeDirectory)
            {
                await LoadRemoteAsync(session.HomeDirectory, reportErrors: true);
            }

            // Fichiers modifiés pendant la coupure : renvoyés maintenant.
            foreach (var edit in _edits.Where(e => e.Pending).ToList())
            {
                await UploadEditAsync(edit);
            }
        }
        catch (OperationCanceledException)
        {
            Fail(SftpStatus.Disconnected, _strings.Get("Terminal_Cancelled"));
        }
        catch (HostKeyRejectedException)
        {
            Fail(SftpStatus.Failed, _strings.Get("Terminal_HostKeyRejected"));
        }
        catch (SshLoginFailedException)
        {
            Fail(SftpStatus.Failed, _strings.Get("Terminal_AuthenticationFailed"));
        }
        catch (PrivateKeyUnavailableException ex)
        {
            Fail(SftpStatus.Failed, _strings.Format("Terminal_KeyUnavailable", ex.Path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Ouverture SFTP vers {Endpoint} impossible ({Error})", Endpoint, ex.GetType().Name);
            Fail(SftpStatus.Failed, _strings.Format("Terminal_ConnectFailed", ex.Message));
        }
        finally
        {
            _connectCancellation = null;
            cancellation.Dispose();
        }
    }

    // ---- Panneau local -----------------------------------------------------------------------

    public void NavigateLocal(string? path)
    {
        var target = path?.Trim().Trim('"') ?? string.Empty;
        if (target.Length > 0)
        {
            target = Environment.ExpandEnvironmentVariables(target);
        }

        LoadLocal(target, reportErrors: true);
    }

    [RelayCommand]
    public void LocalUp()
    {
        var parent = LocalFileSystem.GetParent(LocalPath);
        if (parent is not null)
        {
            LoadLocal(parent, reportErrors: true);
        }
    }

    [RelayCommand]
    public void RefreshLocal() => LoadLocal(LocalPath, reportErrors: true);

    /// <summary>Double-clic sur une ligne locale : entre dans le dossier.</summary>
    public void OpenLocal(FileItemViewModel? item)
    {
        if (item is { IsDirectory: true })
        {
            LoadLocal(item.FullPath, reportErrors: true);
        }
    }

    [RelayCommand]
    public async Task NewLocalFolderAsync()
    {
        if (LocalPath == LocalFileSystem.Computer)
        {
            return;
        }

        var name = await PromptNameAsync("Sftp_NewFolderTitle", _strings.Get("Sftp_NewFolderDefault"), LocalFileSystem.IsValidName);
        if (name is null)
        {
            return;
        }

        await RunLocalAsync(() => Directory.CreateDirectory(Path.Combine(LocalPath, name)));
    }

    public async Task RenameLocalAsync(FileItemViewModel? item)
    {
        if (item is null || item.IsParent || LocalPath == LocalFileSystem.Computer)
        {
            return;
        }

        var name = await PromptNameAsync("Sftp_RenameTitle", item.Name, LocalFileSystem.IsValidName);
        if (name is null || name == item.Name)
        {
            return;
        }

        var target = Path.Combine(LocalPath, name);
        await RunLocalAsync(() =>
        {
            if (item.IsDirectory)
            {
                Directory.Move(item.FullPath, target);
            }
            else
            {
                File.Move(item.FullPath, target);
            }
        });
    }

    /// <summary>Envoie des éléments locaux à la corbeille, après confirmation.</summary>
    public async Task DeleteLocalAsync(IEnumerable<FileItemViewModel> items)
    {
        var targets = Selectable(items);
        if (targets.Count == 0 || _recycler is null || LocalPath == LocalFileSystem.Computer)
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync(
            _strings.Get("Sftp_DeleteLocalTitle"),
            DescribeSelection("Sftp_DeleteLocalOne", "Sftp_DeleteLocalMany", targets),
            _strings.Get("Sftp_Recycle"),
            isDestructive: true))
        {
            return;
        }

        await RunLocalAsync(() =>
        {
            foreach (var item in targets)
            {
                _recycler.Recycle(item.FullPath);
            }
        });
    }

    // ---- Panneau distant ---------------------------------------------------------------------

    public Task NavigateRemoteAsync(string? path) =>
        IsConnected ? LoadRemoteAsync(RemotePath.Length == 0 ? Sftp.RemotePath.Normalize(path) : Sftp.RemotePath.Normalize(path, RemotePath), reportErrors: true) : Task.CompletedTask;

    [RelayCommand]
    public Task RemoteUpAsync()
    {
        var parent = Sftp.RemotePath.GetParent(RemotePath);
        return parent is null || !IsConnected ? Task.CompletedTask : LoadRemoteAsync(parent, reportErrors: true);
    }

    [RelayCommand]
    public Task RefreshRemoteAsync() => IsConnected ? LoadRemoteAsync(RemotePath, reportErrors: true) : Task.CompletedTask;

    /// <summary>Double-clic sur une ligne distante : entre dans le dossier.</summary>
    public Task OpenRemoteAsync(FileItemViewModel? item) =>
        item is { IsDirectory: true } && IsConnected ? LoadRemoteAsync(item.FullPath, reportErrors: true) : Task.CompletedTask;

    [RelayCommand]
    public async Task NewRemoteFolderAsync()
    {
        if (_session is not { } session || !IsConnected)
        {
            return;
        }

        var name = await PromptNameAsync("Sftp_NewFolderTitle", _strings.Get("Sftp_NewFolderDefault"), Sftp.RemotePath.IsValidName);
        if (name is null)
        {
            return;
        }

        await RunRemoteAsync(() => session.CreateDirectoryAsync(Sftp.RemotePath.Combine(RemotePath, name)));
    }

    public async Task RenameRemoteAsync(FileItemViewModel? item)
    {
        if (item?.Remote is not { } entry || _engine is not { } engine || !IsConnected)
        {
            return;
        }

        var name = await PromptNameAsync("Sftp_RenameTitle", item.Name, Sftp.RemotePath.IsValidName);
        if (name is null || name == item.Name)
        {
            return;
        }

        await RunRemoteAsync(() => engine.RenameAsync(entry, Sftp.RemotePath.Combine(RemotePath, name)));
    }

    /// <summary>Supprime des éléments distants (dossiers : récursivement), toujours après confirmation.</summary>
    public async Task DeleteRemoteAsync(IEnumerable<FileItemViewModel> items)
    {
        var targets = Selectable(items).Where(i => i.Remote is not null).ToList();
        if (targets.Count == 0 || _engine is not { } engine || !IsConnected)
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync(
            _strings.Get("Sftp_DeleteRemoteTitle"),
            DescribeSelection("Sftp_DeleteRemoteOne", "Sftp_DeleteRemoteMany", targets),
            _strings.Get("Common_Delete"),
            isDestructive: true))
        {
            return;
        }

        await RunRemoteAsync(async () =>
        {
            foreach (var item in targets)
            {
                await engine.DeleteAsync(item.Remote!);
            }
        });
    }

    public async Task EditPermissionsAsync(FileItemViewModel? item)
    {
        if (item?.Remote is not { } entry || _session is not { } session || !IsConnected)
        {
            return;
        }

        var editor = new PermissionsEditorViewModel(item.Name, entry.Permissions);
        if (!await _dialogs.EditPermissionsAsync(editor) || editor.Mode == (entry.Permissions & UnixPermissions.Mask))
        {
            return;
        }

        await RunRemoteAsync(() => session.ChangePermissionsAsync(entry.FullPath, editor.Mode));
    }

    // ---- Modification d'un fichier distant dans un éditeur local --------------------------------

    /// <summary>
    /// « Modifier » : télécharge le fichier dans un dossier temporaire, l'ouvre dans l'application associée (ou au
    /// choix), puis renvoie chaque enregistrement sur le serveur tant que l'onglet reste ouvert.
    /// </summary>
    public async Task EditRemoteAsync(FileItemViewModel? item, bool chooseApplication = false)
    {
        if (item?.Remote is not { IsDirectory: false } entry || _session is not { } session || !IsConnected
            || _launcher is not { } launcher || _editFiles is not { } files)
        {
            return;
        }

        // Déjà ouvert : on rouvre la même copie (une seule copie par fichier, pas d'enregistrements concurrents).
        if (_edits.FirstOrDefault(e => e.RemotePath == entry.FullPath) is { } open)
        {
            await LaunchAsync(launcher, open, chooseApplication);
            return;
        }

        if (entry.Size > LargeEditSize && !await _dialogs.ConfirmAsync(
            _strings.Get("Sftp_EditLargeTitle"),
            _strings.Format("Sftp_EditLargeMessage", entry.Name, _sizes.Format(entry.Size)),
            _strings.Get("Sftp_Edit")))
        {
            return;
        }

        string local;
        RemoteFileStamp stamp;
        IsRemoteLoading = true;
        try
        {
            local = files.NewLocalPath(entry.Name);
            await session.DownloadFileAsync(entry.FullPath, local);
            stamp = await session.GetStampAsync(entry.FullPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning("Ouverture pour modification impossible ({Error})", ex.GetType().Name);
            await _dialogs.ShowErrorAsync(_strings.Get("Sftp_ErrorTitle"), _strings.Format("Sftp_RemoteError", Describe(ex)));
            return;
        }
        finally
        {
            IsRemoteLoading = false;
        }

        var edit = new RemoteEdit(entry.FullPath, local, stamp);
        edit.Saved += saved => _dispatcher.Post(() => _ = UploadEditAsync(saved));
        edit.Start();
        _edits.Add(edit);
        UpdateEdits();
        await LaunchAsync(launcher, edit, chooseApplication);
    }

    /// <summary>Arrête de suivre les fichiers ouverts : les prochains enregistrements ne sont plus renvoyés.</summary>
    [RelayCommand]
    public void StopEdits()
    {
        foreach (var edit in _edits)
        {
            edit.Dispose();
        }

        _edits.Clear();
        UpdateEdits();
    }

    /// <summary>Test : fichiers ouverts pour modification.</summary>
    internal IReadOnlyList<RemoteEdit> Edits => _edits;

    /// <summary>
    /// Renvoie la copie locale enregistrée. Si le fichier a changé sur le serveur depuis son ouverture, l'utilisateur
    /// choisit d'écraser ou non. Envoi en place (droits et propriétaire conservés), non interruptible.
    /// </summary>
    internal async Task UploadEditAsync(RemoteEdit edit)
    {
        if (_disposed || !_edits.Contains(edit))
        {
            return;
        }

        if (edit.Uploading)
        {
            edit.UploadAgain = true;
            return;
        }

        if (_session is not { } session || !IsConnected)
        {
            edit.Pending = true;
            UpdateEdits();
            return;
        }

        edit.Uploading = true;
        var transfer = new TransferItemViewModel(TransferDirection.Upload, edit.Name, edit.LocalPath, edit.RemoteDirectory, cancellable: false)
        {
            Status = TransferStatus.Running,
            Detail = _strings.Get("Sftp_EditSending"),
        };
        try
        {
            RemoteFileStamp? current;
            try
            {
                current = await session.GetStampAsync(edit.RemotePath);
            }
            catch (SftpPathNotFoundException)
            {
                current = null;
            }

            if (current is not { } now || !now.SameAs(edit.RemoteStamp))
            {
                var confirmed = await _dialogs.ConfirmAsync(
                    _strings.Get("Sftp_EditConflictTitle"),
                    _strings.Format(current is null ? "Sftp_EditDeletedMessage" : "Sftp_EditConflictMessage", edit.Name),
                    _strings.Get("Sftp_EditOverwrite"),
                    isDestructive: true);
                if (!confirmed || _disposed || !_edits.Contains(edit))
                {
                    // Le prochain enregistrement reposera la question.
                    return;
                }
            }

            var snapshot = await Task.Run(edit.Snapshot);
            if (snapshot is not { } copy)
            {
                throw new IOException(_strings.Format("Sftp_EditUnreadable", edit.Name));
            }

            Transfers.Add(transfer);
            HasTransfers = true;
            transfer.TotalBytes = copy.Size;
            try
            {
                await session.UploadFileAsync(copy.Path, edit.RemotePath);
            }
            finally
            {
                TryDelete(copy.Path);
            }

            edit.MarkSent(copy.Hash);
            edit.RemoteStamp = await session.GetStampAsync(edit.RemotePath);
            transfer.TransferredBytes = copy.Size;
            transfer.Status = TransferStatus.Completed;
            transfer.Detail = _strings.Format("Sftp_EditSent", _sizes.Format(copy.Size));
            if (IsConnected && edit.RemoteDirectory == RemotePath)
            {
                await LoadRemoteAsync(RemotePath, reportErrors: false);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning("Renvoi d'un fichier modifié en échec ({Error})", ex.GetType().Name);
            edit.Pending = true;
            if (!Transfers.Contains(transfer))
            {
                Transfers.Add(transfer);
                HasTransfers = true;
            }

            transfer.Status = TransferStatus.Failed;
            transfer.Detail = _strings.Format("Sftp_EditFailed", Describe(ex));
        }
        finally
        {
            edit.Uploading = false;
            UpdateEdits();
            if (edit.UploadAgain)
            {
                edit.UploadAgain = false;
                if (edit.HasLocalChanges())
                {
                    await UploadEditAsync(edit);
                }
            }
        }
    }

    private async Task LaunchAsync(IFileLauncher launcher, RemoteEdit edit, bool chooseApplication)
    {
        bool launched;
        try
        {
            launched = await launcher.OpenAsync(edit.LocalPath, chooseApplication);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning("Lancement de l'éditeur impossible ({Error})", ex.GetType().Name);
            launched = false;
        }

        if (!launched)
        {
            await _dialogs.ShowErrorAsync(_strings.Get("Sftp_ErrorTitle"), _strings.Format("Sftp_EditLaunchFailed", edit.Name));
        }
    }

    private void UpdateEdits()
    {
        HasEdits = _edits.Count > 0;
        var pending = _edits.Count(e => e.Pending);
        EditsText = _edits.Count switch
        {
            0 => string.Empty,
            1 => _strings.Format(pending > 0 ? "Sftp_EditsOnePending" : "Sftp_EditsOne", _edits[0].Name),
            _ => pending > 0
                ? _strings.Format("Sftp_EditsManyPending", _edits.Count, pending)
                : _strings.Format("Sftp_EditsMany", _edits.Count, string.Join(", ", _edits.Take(3).Select(e => e.Name)) + (_edits.Count > 3 ? ", …" : string.Empty)),
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Copie temporaire : supprimée avec son dossier à la fin de la modification.
        }
    }

    // ---- Transferts --------------------------------------------------------------------------

    /// <summary>Envoie des fichiers ou dossiers locaux dans le dossier distant affiché.</summary>
    public async Task UploadAsync(IEnumerable<string> localPaths)
    {
        if (!IsConnected)
        {
            return;
        }

        var sources = localPaths
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Select(p => (Path: p, Name: Path.GetFileName(Path.TrimEndingDirectorySeparator(p))))
            .Where(s => s.Name.Length > 0)
            .ToList();
        if (sources.Count == 0)
        {
            return;
        }

        var existing = RemoteItems.Where(i => !i.IsParent).Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        if (!await ConfirmOverwriteAsync([.. sources.Select(s => s.Name).Where(existing.Contains)]))
        {
            return;
        }

        var destination = RemotePath;
        foreach (var (path, name) in sources)
        {
            Enqueue(new TransferItemViewModel(TransferDirection.Upload, name, path, destination));
        }
    }

    /// <summary>Envoie les éléments sélectionnés du panneau local.</summary>
    public Task UploadAsync(IEnumerable<FileItemViewModel> items) =>
        UploadAsync(Selectable(items).Select(i => i.FullPath));

    /// <summary>Récupère des éléments distants dans le dossier local affiché.</summary>
    public async Task DownloadAsync(IEnumerable<FileItemViewModel> items)
    {
        var targets = Selectable(items).Where(i => i.Remote is not null).ToList();
        if (targets.Count == 0 || !IsConnected || LocalPath == LocalFileSystem.Computer)
        {
            return;
        }

        var existing = LocalItems.Where(i => !i.IsParent).Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!await ConfirmOverwriteAsync([.. targets.Select(t => SftpTransferEngine.ToLocalName(t.Name)).Where(existing.Contains)]))
        {
            return;
        }

        var destination = LocalPath;
        foreach (var item in targets)
        {
            Enqueue(new TransferItemViewModel(TransferDirection.Download, item.Name, item.FullPath, destination, item.Remote));
        }
    }

    [RelayCommand]
    public void ClearFinishedTransfers()
    {
        foreach (var finished in Transfers.Where(t => !t.IsActive).ToList())
        {
            Transfers.Remove(finished);
        }

        HasTransfers = Transfers.Count > 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connectCancellation?.Cancel();
        StopEdits();
        foreach (var transfer in Transfers)
        {
            transfer.Cancel();
        }

        // Le transfert interrompu supprime son fichier incomplet avec la session encore ouverte :
        // on lui laisse le temps de le faire (borné, la connexion peut ne plus répondre) avant de la fermer.
        await Task.WhenAny(_pump, Task.Delay(TransferCleanupTimeout));

        var session = Detach();
        if (session is not null)
        {
            await session.DisposeAsync();
        }
    }

    // ---- Détails -----------------------------------------------------------------------------

    private static List<FileItemViewModel> Selectable(IEnumerable<FileItemViewModel> items) =>
        [.. items.Where(i => i is { IsParent: false })];

    private void Enqueue(TransferItemViewModel transfer)
    {
        transfer.Detail = _strings.Get("Transfer_Pending");
        Transfers.Add(transfer);
        HasTransfers = true;
        if (!_pumping)
        {
            _pump = PumpAsync();
        }
    }

    /// <summary>Exécute les transferts en attente, un par un (une seule connexion SFTP).</summary>
    private async Task PumpAsync()
    {
        if (_pumping)
        {
            return;
        }

        _pumping = true;
        try
        {
            while (!_disposed && Transfers.FirstOrDefault(t => t.Status == TransferStatus.Pending) is { } next)
            {
                await RunTransferAsync(next);
            }
        }
        finally
        {
            _pumping = false;
        }
    }

    private async Task RunTransferAsync(TransferItemViewModel transfer)
    {
        if (_engine is not { } engine || !IsConnected)
        {
            transfer.Status = TransferStatus.Failed;
            transfer.Detail = _strings.Get("Sftp_NotConnected");
            return;
        }

        transfer.Status = TransferStatus.Running;
        var progress = new ThrottledProgress(_dispatcher, bytes =>
        {
            if (transfer.Status == TransferStatus.Running)
            {
                transfer.TransferredBytes = bytes;
                transfer.Detail = $"{_sizes.Format(bytes)} / {_sizes.Format(transfer.TotalBytes)}";
            }
        });

        TransferStatus outcome;
        string detail;
        try
        {
            var token = transfer.CancellationToken;
            if (transfer.Direction == TransferDirection.Upload)
            {
                transfer.TotalBytes = await SftpTransferEngine.MeasureLocalAsync(transfer.Source, token);
                await engine.UploadAsync(transfer.Source, transfer.DestinationDirectory, progress, token);
            }
            else
            {
                transfer.TotalBytes = await engine.MeasureRemoteAsync(transfer.Remote!, token);
                await engine.DownloadAsync(transfer.Remote!, transfer.DestinationDirectory, progress, token);
            }

            transfer.TransferredBytes = transfer.TotalBytes;
            outcome = TransferStatus.Completed;
            detail = _strings.Format("Transfer_Completed", _sizes.Format(transfer.TotalBytes));
        }
        catch (OperationCanceledException)
        {
            outcome = TransferStatus.Cancelled;
            detail = _strings.Get("Transfer_Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Transfert en échec ({Error})", ex.GetType().Name);
            outcome = TransferStatus.Failed;
            detail = _strings.Format("Transfer_Failed", Describe(ex));
        }

        try
        {
            // Le panneau de destination est rafraîchi s'il affiche toujours le dossier concerné, avant d'annoncer
            // la fin du transfert : un transfert terminé est visible dans son panneau.
            if (_disposed)
            {
                return;
            }

            if (transfer.Direction == TransferDirection.Upload)
            {
                if (IsConnected && transfer.DestinationDirectory == RemotePath)
                {
                    await LoadRemoteAsync(RemotePath, reportErrors: false);
                }
            }
            else if (transfer.DestinationDirectory == LocalPath)
            {
                LoadLocal(LocalPath, reportErrors: false);
            }
        }
        finally
        {
            transfer.Status = outcome;
            transfer.Detail = detail;
        }
    }

    private async Task<bool> ConfirmOverwriteAsync(IReadOnlyList<string> conflicts)
    {
        if (conflicts.Count == 0)
        {
            return true;
        }

        var message = conflicts.Count == 1
            ? _strings.Format("Sftp_OverwriteOne", conflicts[0])
            : _strings.Format("Sftp_OverwriteMany", conflicts.Count, string.Join(", ", conflicts.Take(5)));
        return await _dialogs.ConfirmAsync(_strings.Get("Sftp_OverwriteTitle"), message, _strings.Get("Sftp_Overwrite"), isDestructive: true);
    }

    private string DescribeSelection(string oneKey, string manyKey, IReadOnlyList<FileItemViewModel> items) =>
        items.Count == 1
            ? _strings.Format(oneKey, items[0].Name)
            : _strings.Format(manyKey, items.Count, string.Join(", ", items.Take(5).Select(i => i.Name)));

    private async Task<string?> PromptNameAsync(string titleKey, string initial, Func<string?, bool> isValid)
    {
        var name = (await _dialogs.PromptTextAsync(
            _strings.Get(titleKey),
            _strings.Get("Sftp_NameLabel"),
            initial,
            _strings.Get("Common_Ok")))?.Trim();
        if (name is null)
        {
            return null;
        }

        if (!isValid(name))
        {
            await _dialogs.ShowErrorAsync(_strings.Get("Sftp_ErrorTitle"), _strings.Format("Sftp_InvalidName", name));
            return null;
        }

        return name;
    }

    private void LoadLocal(string path, bool reportErrors)
    {
        try
        {
            var full = path == LocalFileSystem.Computer ? path : Path.GetFullPath(path);
            var entries = LocalFileSystem.List(full);

            LocalItems.Clear();
            if (LocalFileSystem.GetParent(full) is { } parent)
            {
                LocalItems.Add(FileItemViewModel.Parent(parent));
            }

            foreach (var entry in entries)
            {
                LocalItems.Add(FileItemViewModel.FromLocal(entry, _sizes));
            }

            LocalPath = full;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            if (reportErrors)
            {
                _ = _dialogs.ShowErrorAsync(_strings.Get("Sftp_ErrorTitle"), _strings.Format("Sftp_LocalError", ex.Message));
            }

            // Le champ de saisie revient au dossier réellement affiché.
            OnPropertyChanged(nameof(LocalPath));
        }
    }

    private async Task RunLocalAsync(Action action)
    {
        try
        {
            await Task.Run(action);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OperationCanceledException)
        {
            await _dialogs.ShowErrorAsync(_strings.Get("Sftp_ErrorTitle"), _strings.Format("Sftp_LocalError", ex.Message));
        }

        LoadLocal(LocalPath, reportErrors: false);
    }

    private async Task<bool> LoadRemoteAsync(string path, bool reportErrors)
    {
        if (_session is not { } session)
        {
            return false;
        }

        IsRemoteLoading = true;
        try
        {
            var entries = await session.ListAsync(path);

            RemoteItems.Clear();
            if (Sftp.RemotePath.GetParent(path) is { } parent)
            {
                RemoteItems.Add(FileItemViewModel.Parent(parent));
            }

            foreach (var entry in entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                RemoteItems.Add(FileItemViewModel.FromRemote(entry, _sizes));
            }

            RemotePath = path;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (reportErrors)
            {
                await _dialogs.ShowErrorAsync(_strings.Get("Sftp_ErrorTitle"), _strings.Format("Sftp_RemoteError", Describe(ex)));
            }

            OnPropertyChanged(nameof(RemotePath));
            return false;
        }
        finally
        {
            IsRemoteLoading = false;
        }
    }

    /// <summary>Exécute une opération distante, signale l'échec éventuel, puis rafraîchit le panneau.</summary>
    private async Task RunRemoteAsync(Func<Task> action)
    {
        IsRemoteLoading = true;
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await _dialogs.ShowErrorAsync(_strings.Get("Sftp_ErrorTitle"), _strings.Format("Sftp_RemoteError", Describe(ex)));
        }
        finally
        {
            IsRemoteLoading = false;
        }

        if (IsConnected)
        {
            await LoadRemoteAsync(RemotePath, reportErrors: false);
        }
    }

    private string Describe(Exception error) => error switch
    {
        SftpPermissionDeniedException => _strings.Get("Sftp_PermissionDenied"),
        SftpPathNotFoundException => _strings.Get("Sftp_PathNotFound"),
        UnauthorizedAccessException => _strings.Get("Sftp_PermissionDenied"),
        _ => error.Message,
    };

    private ISftpSession? Detach()
    {
        var session = _session;
        _session = null;
        _engine = null;
        if (session is not null)
        {
            session.Closed -= OnSessionClosed;
        }

        return session;
    }

    private void OnSessionClosed(Exception? error)
    {
        // Notifié depuis le fil réseau : on repasse sur le fil d'interface.
        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            var session = Detach();
            if (session is not null)
            {
                _ = session.DisposeAsync().AsTask();
            }

            Fail(
                SftpStatus.Disconnected,
                error is null ? _strings.Get("Terminal_Closed") : _strings.Format("Terminal_ConnectionLost", error.Message));
        });
    }

    private void Fail(SftpStatus status, string message)
    {
        Status = status;
        StatusText = message;
    }

    /// <summary>
    /// Progression rapportée depuis les fils réseau : seule la dernière valeur est publiée sur le fil
    /// d'interface, au plus une publication en attente à la fois.
    /// </summary>
    private sealed class ThrottledProgress(IUiDispatcher dispatcher, Action<long> apply) : IProgress<long>
    {
        private long _latest;
        private int _queued;

        public void Report(long value)
        {
            Interlocked.Exchange(ref _latest, value);
            if (Interlocked.Exchange(ref _queued, 1) == 0)
            {
                dispatcher.Post(() =>
                {
                    Interlocked.Exchange(ref _queued, 0);
                    apply(Interlocked.Read(ref _latest));
                });
            }
        }
    }
}
