using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace Quai.Core.Sftp;

/// <summary>Session SFTP au-dessus de <see cref="SftpClient"/> (SSH.NET).</summary>
internal sealed class SshSftpSession : ISftpSession
{
    private const int FileBufferSize = 128 * 1024;

    private readonly SftpClient _client;
    private int _closed;

    public SshSftpSession(SftpClient client)
    {
        _client = client;
        _client.OperationTimeout = TimeSpan.FromSeconds(60);
        _client.ErrorOccurred += (_, e) => RaiseClosed(e.Exception);
        HomeDirectory = RemotePath.Normalize(_client.WorkingDirectory);
    }

    public event Action<Exception?>? Closed;

    public bool IsConnected => Volatile.Read(ref _closed) == 0 && _client.IsConnected;

    public string HomeDirectory { get; }

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        var entries = new List<RemoteEntry>();
        await foreach (var file in _client.ListDirectoryAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (file.Name is "." or "..")
            {
                continue;
            }

            var isDirectory = file.IsDirectory;
            if (file.IsSymbolicLink)
            {
                // Un lien vers un dossier se parcourt comme un dossier.
                isDirectory = await IsDirectoryAsync(file.FullName, cancellationToken).ConfigureAwait(false);
            }

            entries.Add(new RemoteEntry(
                file.Name,
                RemotePath.Combine(path, file.Name),
                isDirectory,
                file.IsSymbolicLink,
                file.Length,
                file.LastWriteTime,
                ReadMode(file.Attributes)));
        }

        return entries;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) =>
        _client.ExistsAsync(path, cancellationToken);

    public async Task<RemoteFileStamp> GetStampAsync(string path, CancellationToken cancellationToken = default)
    {
        var attributes = await _client.GetAttributesAsync(path, cancellationToken).ConfigureAwait(false);
        return new RemoteFileStamp(attributes.Size, attributes.LastWriteTimeUtc);
    }

    public async Task UploadFileAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var input = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferSize, useAsync: true);
        var report = progress is null
            ? null
            : new InlineProgress<UploadFileProgressReport>(r => progress.Report((long)r.TotalBytesUploaded));
        await _client.UploadFileAsync(input, remotePath, canOverride: true, report!, cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadFileAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var output = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, FileBufferSize, useAsync: true);
        var report = progress is null
            ? null
            : new InlineProgress<DownloadFileProgressReport>(r => progress.Report((long)r.TotalBytesDownloaded));
        await _client.DownloadFileAsync(remotePath, output, report!, cancellationToken).ConfigureAwait(false);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        _client.CreateDirectoryAsync(path, cancellationToken);

    public Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken = default) =>
        _client.RenameFileAsync(oldPath, newPath, cancellationToken);

    public async Task RenameLinkAsync(string oldPath, string newPath, CancellationToken cancellationToken = default)
    {
        var link = await FindInParentAsync(oldPath, cancellationToken).ConfigureAwait(false);
        await Task.Run(() => link.MoveTo(newPath), cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default) =>
        _client.DeleteFileAsync(path, cancellationToken);

    public async Task DeleteLinkAsync(string path, CancellationToken cancellationToken = default)
    {
        var link = await FindInParentAsync(path, cancellationToken).ConfigureAwait(false);
        await link.DeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        _client.DeleteDirectoryAsync(path, cancellationToken);

    public Task ChangePermissionsAsync(string path, int mode, CancellationToken cancellationToken = default) =>
        Task.Run(() => _client.ChangePermissions(path, UnixPermissions.ToSshNetMode(mode)), cancellationToken);

    public Task SetLastWriteTimeAsync(string path, DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default) =>
        Task.Run(() => _client.SetLastWriteTimeUtc(path, lastWriteTimeUtc), cancellationToken);

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _closed, 1);
        return new ValueTask(Task.Run(() =>
        {
            try
            {
                _client.Dispose();
            }
            catch (Exception)
            {
                // Fermeture au mieux : la connexion peut déjà être rompue.
            }
        }));
    }

    private static int ReadMode(SftpFileAttributes attributes)
    {
        var mode = 0;
        mode |= attributes.OwnerCanRead ? UnixPermissions.OwnerRead : 0;
        mode |= attributes.OwnerCanWrite ? UnixPermissions.OwnerWrite : 0;
        mode |= attributes.OwnerCanExecute ? UnixPermissions.OwnerExecute : 0;
        mode |= attributes.GroupCanRead ? UnixPermissions.GroupRead : 0;
        mode |= attributes.GroupCanWrite ? UnixPermissions.GroupWrite : 0;
        mode |= attributes.GroupCanExecute ? UnixPermissions.GroupExecute : 0;
        mode |= attributes.OthersCanRead ? UnixPermissions.OthersRead : 0;
        mode |= attributes.OthersCanWrite ? UnixPermissions.OthersWrite : 0;
        mode |= attributes.OthersCanExecute ? UnixPermissions.OthersExecute : 0;
        return mode;
    }

    /// <summary>
    /// SSH.NET résout les liens symboliques (realpath) avant la plupart des opérations par chemin :
    /// supprimer ou renommer « le lien » agirait sur sa cible. On repasse donc par l'entrée du dossier
    /// parent, dont le chemin n'est pas résolu.
    /// </summary>
    private async Task<ISftpFile> FindInParentAsync(string path, CancellationToken cancellationToken)
    {
        var parent = RemotePath.GetParent(path) ?? RemotePath.Root;
        var name = RemotePath.GetName(path);
        await foreach (var file in _client.ListDirectoryAsync(parent, cancellationToken).ConfigureAwait(false))
        {
            if (file.Name == name)
            {
                return file;
            }
        }

        throw new Renci.SshNet.Common.SftpPathNotFoundException($"Introuvable : {path}");
    }

    private async Task<bool> IsDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            // GetAttributes suit les liens symboliques (stat, pas lstat).
            return (await _client.GetAttributesAsync(path, cancellationToken).ConfigureAwait(false)).IsDirectory;
        }
        catch (Exception ex) when (ex is Renci.SshNet.Common.SshException)
        {
            return false;
        }
    }

    private void RaiseClosed(Exception? error)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            Closed?.Invoke(error);
        }
    }

    /// <summary>Progression rapportée en ligne, sur le fil de SSH.NET (pas de contexte de synchronisation).</summary>
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
