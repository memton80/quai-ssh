using System.Security.Cryptography;

namespace Quai.Core.Sftp;

/// <summary>
/// Copie locale d'un fichier distant ouverte dans un éditeur (« Modifier » dans l'explorateur SFTP, à la WinSCP) :
/// le fichier est surveillé, et chaque enregistrement dont le contenu a changé est signalé (<see cref="Saved"/>)
/// pour être renvoyé sur le serveur. Les éditeurs enregistrent souvent par fichier temporaire puis renommage :
/// tout le dossier (propre à cette copie) est surveillé, et les rafales d'événements sont regroupées.
/// </summary>
public sealed class RemoteEdit : IDisposable
{
    /// <summary>Attente après le dernier événement avant de relire le fichier (rafales d'un même enregistrement).</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(600);

    private readonly TimeSpan _debounce;
    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private byte[] _sentHash;
    private bool _disposed;

    public RemoteEdit(string remotePath, string localPath, RemoteFileStamp remoteStamp, TimeSpan? debounce = null)
    {
        RemotePath = remotePath;
        LocalPath = localPath;
        RemoteStamp = remoteStamp;
        _debounce = debounce ?? DefaultDebounce;
        _sentHash = HashOf(localPath) ?? [];
    }

    /// <summary>
    /// Le contenu local diffère de ce qui est sur le serveur (déclenché hors du fil d'interface, après regroupement).
    /// </summary>
    public event Action<RemoteEdit>? Saved;

    public string RemotePath { get; }

    public string LocalPath { get; }

    public string Name => Sftp.RemotePath.GetName(RemotePath);

    /// <summary>Dossier distant du fichier.</summary>
    public string RemoteDirectory => Sftp.RemotePath.GetParent(RemotePath) ?? Sftp.RemotePath.Root;

    /// <summary>État connu du fichier sur le serveur (au téléchargement, puis après chaque envoi).</summary>
    public RemoteFileStamp RemoteStamp { get; set; }

    /// <summary>Enregistré localement mais pas encore renvoyé (session coupée, envoi en échec).</summary>
    public bool Pending { get; set; }

    /// <summary>Envoi en cours ; un nouvel enregistrement pendant l'envoi est renvoyé juste après.</summary>
    internal bool Uploading { get; set; }

    internal bool UploadAgain { get; set; }

    /// <summary>Dossier propre à cette copie (supprimé à la fin de la modification).</summary>
    public string LocalDirectory => Path.GetDirectoryName(LocalPath)!;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _watcher is not null)
            {
                return;
            }

            _timer = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
            _watcher = new FileSystemWatcher(LocalDirectory)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>Le contenu local a changé depuis le dernier envoi (ou le téléchargement).</summary>
    public bool HasLocalChanges() => HashOf(LocalPath) is { } hash && !hash.AsSpan().SequenceEqual(_sentHash);

    /// <summary>
    /// Copie figée du fichier local à envoyer (l'éditeur peut réécrire l'original pendant l'envoi), avec son empreinte.
    /// Retourne <c>null</c> si le fichier est illisible (encore verrouillé par l'éditeur, supprimé).
    /// </summary>
    public (string Path, byte[] Hash, long Size)? Snapshot()
    {
        var snapshot = Path.Combine(LocalDirectory, ".quai-envoi");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.Copy(LocalPath, snapshot, overwrite: true);
                var hash = HashOf(snapshot);
                return hash is null ? null : (snapshot, hash, new FileInfo(snapshot).Length);
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(150);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Ce contenu est désormais celui du serveur.</summary>
    public void MarkSent(byte[] hash)
    {
        lock (_gate)
        {
            _sentHash = hash;
        }

        Pending = false;
    }

    /// <summary>Arrête la surveillance et supprime la copie locale (au mieux : l'éditeur peut la garder ouverte).</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watcher?.Dispose();
            _timer?.Dispose();
        }

        try
        {
            Directory.Delete(LocalDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fichier encore ouvert : le dossier sera nettoyé au prochain démarrage.
        }
    }

    internal static byte[]? HashOf(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return SHA256.HashData(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!string.Equals(e.FullPath, LocalPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_gate)
        {
            // Chaque événement repousse la relecture : un enregistrement en plusieurs écritures n'est envoyé qu'une fois.
            _timer?.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Check()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        if (HasLocalChanges())
        {
            Saved?.Invoke(this);
        }
    }
}

/// <summary>Dossier des copies locales en cours de modification (<c>%LOCALAPPDATA%\Quai\edits</c>).</summary>
public sealed class RemoteEditFiles
{
    public RemoteEditFiles(string root)
    {
        Root = root;
    }

    public string Root { get; }

    /// <summary>Nouveau dossier pour une copie : le nom du fichier reste celui du serveur (l'éditeur l'affiche).</summary>
    public string NewLocalPath(string remoteName)
    {
        var directory = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, SftpTransferEngine.ToLocalName(remoteName));
    }

    /// <summary>Copies laissées par une session précédente (fermeture brutale) : supprimées au démarrage.</summary>
    public void CleanUp()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un fichier encore ouvert ailleurs : on réessaiera au prochain démarrage.
        }
    }
}
