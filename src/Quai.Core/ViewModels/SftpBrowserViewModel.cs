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
    private readonly ISshSessionFactory _factory;
    private readonly IDialogService _dialogs;
    private readonly IStrings _strings;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILocalRecycler? _recycler;
    private readonly ILogger _logger;
    private readonly FileSizeFormatter _sizes;
    private ISftpSession? _session;
    private SftpTransferEngine? _engine;
    private CancellationTokenSource? _connectCancellation;
    private bool _pumping;
    private bool _disposed;

    public SftpBrowserViewModel(
        ConnectionProfile profile,
        ISshSessionFactory factory,
        IDialogService dialogs,
        IStrings strings,
        IUiDispatcher dispatcher,
        ILocalRecycler? recycler = null,
        ILogger? logger = null,
        string? initialLocalPath = null)
    {
        Profile = profile;
        _factory = factory;
        _dialogs = dialogs;
        _strings = strings;
        _dispatcher = dispatcher;
        _recycler = recycler;
        _logger = logger ?? NullLogger.Instance;
        _sizes = new FileSizeFormatter(strings);

        Status = SftpStatus.Idle;
        StatusText = string.Empty;
        LocalPath = initialLocalPath ?? LocalFileSystem.HomeDirectory;
        RemotePath = string.Empty;
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
        foreach (var transfer in Transfers)
        {
            transfer.Cancel();
        }

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
        _ = PumpAsync();
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
            transfer.Status = TransferStatus.Completed;
            transfer.Detail = _strings.Format("Transfer_Completed", _sizes.Format(transfer.TotalBytes));
        }
        catch (OperationCanceledException)
        {
            transfer.Status = TransferStatus.Cancelled;
            transfer.Detail = _strings.Get("Transfer_Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Transfert en échec ({Error})", ex.GetType().Name);
            transfer.Status = TransferStatus.Failed;
            transfer.Detail = _strings.Format("Transfer_Failed", Describe(ex));
        }

        // Le panneau de destination est rafraîchi s'il affiche toujours le dossier concerné.
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
