using Quai.Core.Services;
using Quai.Core.Sftp;
using Renci.SshNet.Common;

namespace Quai.Core.Tests.Support;

/// <summary>Serveur SFTP en mémoire pour tester l'explorateur sans réseau.</summary>
public sealed class FakeSftpSession : ISftpSession
{
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal)
    {
        ["/"] = new Node { IsDirectory = true },
        ["/home"] = new Node { IsDirectory = true },
        ["/home/alice"] = new Node { IsDirectory = true },
    };

    public event Action<Exception?>? Closed;

    public bool IsConnected { get; private set; } = true;

    public bool Disposed { get; private set; }

    public string HomeDirectory => "/home/alice";

    /// <summary>Journal des opérations de modification (« delete /a », « chmod 755 /b »…).</summary>
    public List<string> Operations { get; } = [];

    /// <summary>Si défini, chaque transfert attend ce signal après avoir rapporté une première progression.</summary>
    public TaskCompletionSource? TransferGate { get; set; }

    /// <summary>Erreur levée par la prochaine opération de modification ou de transfert.</summary>
    public Exception? NextError { get; set; }

    public void AddDirectory(string path) => _nodes[path] = new Node { IsDirectory = true };

    public void AddFile(string path, string content = "", int mode = 0b110_100_100) =>
        _nodes[path] = new Node { Content = System.Text.Encoding.UTF8.GetBytes(content), Mode = mode };

    public void AddLink(string path, bool toDirectory = false) =>
        _nodes[path] = new Node { IsLink = true, IsDirectory = toDirectory };

    public bool Contains(string path) => _nodes.ContainsKey(path);

    public string ReadText(string path) => System.Text.Encoding.UTF8.GetString(_nodes[path].Content);

    public int ModeOf(string path) => _nodes[path].Mode;

    public void Drop(Exception? error = null)
    {
        IsConnected = false;
        Closed?.Invoke(error);
    }

    public Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!_nodes.TryGetValue(path, out var directory) || !directory.IsDirectory)
        {
            return Task.FromException<IReadOnlyList<RemoteEntry>>(new SftpPathNotFoundException("No such file"));
        }

        var prefix = path == "/" ? "/" : path + "/";
        IReadOnlyList<RemoteEntry> entries = [.. _nodes
            .Where(n => n.Key != "/" && n.Key.StartsWith(prefix, StringComparison.Ordinal) && !n.Key[prefix.Length..].Contains('/'))
            .Select(n => new RemoteEntry(
                n.Key[prefix.Length..],
                n.Key,
                n.Value.IsDirectory,
                n.Value.IsLink,
                n.Value.Content.Length,
                n.Value.Modified,
                n.Value.Mode))];
        return Task.FromResult(entries);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(_nodes.ContainsKey(path));

    public async Task UploadFileAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ThrowPending();
        var content = await File.ReadAllBytesAsync(localPath, cancellationToken);
        await SimulateTransferAsync(content.Length, progress, cancellationToken);
        _nodes[remotePath] = new Node { Content = content };
        Operations.Add($"upload {remotePath}");
    }

    public async Task DownloadFileAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ThrowPending();
        var content = _nodes[remotePath].Content;
        await File.WriteAllBytesAsync(localPath, [], cancellationToken);
        await SimulateTransferAsync(content.Length, progress, cancellationToken);
        await File.WriteAllBytesAsync(localPath, content, cancellationToken);
        Operations.Add($"download {remotePath}");
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Mutate($"mkdir {path}", () =>
    {
        if (_nodes.ContainsKey(path))
        {
            throw new SshException("Failure");
        }

        AddDirectory(path);
    });

    public Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken = default) =>
        Mutate($"rename {oldPath} {newPath}", () => Move(oldPath, newPath));

    public Task RenameLinkAsync(string oldPath, string newPath, CancellationToken cancellationToken = default) =>
        Mutate($"rename-link {oldPath} {newPath}", () => Move(oldPath, newPath));

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default) =>
        Mutate($"delete {path}", () => _nodes.Remove(path));

    public Task DeleteLinkAsync(string path, CancellationToken cancellationToken = default) =>
        Mutate($"delete-link {path}", () => _nodes.Remove(path));

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default) => Mutate($"rmdir {path}", () =>
    {
        if (_nodes.Keys.Any(k => k.StartsWith(path + "/", StringComparison.Ordinal)))
        {
            throw new SshException("Directory not empty");
        }

        _nodes.Remove(path);
    });

    public Task ChangePermissionsAsync(string path, int mode, CancellationToken cancellationToken = default) =>
        Mutate($"chmod {UnixPermissions.ToOctal(mode)} {path}", () => _nodes[path].Mode = mode);

    public Task SetLastWriteTimeAsync(string path, DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default)
    {
        _nodes[path].Modified = lastWriteTimeUtc.ToLocalTime();
        return Task.CompletedTask;
    }

    /// <summary>Opérations déjà effectuées au moment de la fermeture de la session.</summary>
    public IReadOnlyList<string>? OperationsAtDispose { get; private set; }

    public ValueTask DisposeAsync()
    {
        OperationsAtDispose ??= [.. Operations];
        Disposed = true;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    private async Task SimulateTransferAsync(long size, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(size / 2);
        if (TransferGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(size);
    }

    private Task Mutate(string operation, Action action)
    {
        try
        {
            ThrowPending();
            action();
            Operations.Add(operation);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private void Move(string oldPath, string newPath)
    {
        foreach (var key in _nodes.Keys.Where(k => k == oldPath || k.StartsWith(oldPath + "/", StringComparison.Ordinal)).ToList())
        {
            _nodes[newPath + key[oldPath.Length..]] = _nodes[key];
            _nodes.Remove(key);
        }
    }

    private void ThrowPending()
    {
        if (NextError is { } error)
        {
            NextError = null;
            throw error;
        }
    }

    private sealed class Node
    {
        public bool IsDirectory { get; init; }

        public bool IsLink { get; init; }

        public byte[] Content { get; init; } = [];

        public int Mode { get; set; } = 0b111_101_101;

        public DateTime Modified { get; set; } = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
    }
}

/// <summary>Corbeille factice : note les chemins et supprime réellement (dossier temporaire de test).</summary>
public sealed class FakeRecycler : ILocalRecycler
{
    public List<string> Recycled { get; } = [];

    public void Recycle(string path)
    {
        Recycled.Add(path);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }
    }
}
