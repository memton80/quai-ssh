namespace Quai.Core.Sftp;

/// <summary>Opérations SFTP élémentaires sur une connexion ouverte.</summary>
public interface ISftpSession : IAsyncDisposable
{
    /// <summary>Connexion perdue ; l'exception est nulle pour une fermeture normale.</summary>
    event Action<Exception?>? Closed;

    bool IsConnected { get; }

    /// <summary>Dossier d'arrivée (en général le dossier personnel).</summary>
    string HomeDirectory { get; }

    /// <summary>Contenu du dossier, sans « . » ni « .. ».</summary>
    Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Taille et date de modification d'un fichier (liens suivis) ; sert à voir s'il a changé sur le serveur.</summary>
    Task<RemoteFileStamp> GetStampAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Envoie un fichier (remplace la destination). <paramref name="progress"/> reçoit le cumul d'octets.</summary>
    Task UploadFileAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Récupère un fichier (remplace la destination). <paramref name="progress"/> reçoit le cumul d'octets.</summary>
    Task DownloadFileAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

    Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken = default);

    /// <summary>Renomme le lien symbolique lui-même (et non sa cible).</summary>
    Task RenameLinkAsync(string oldPath, string newPath, CancellationToken cancellationToken = default);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Supprime le lien symbolique lui-même (et non sa cible).</summary>
    Task DeleteLinkAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Supprime un dossier vide.</summary>
    Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default);

    Task ChangePermissionsAsync(string path, int mode, CancellationToken cancellationToken = default);

    Task SetLastWriteTimeAsync(string path, DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default);
}
