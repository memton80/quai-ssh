namespace Quai.Core.Sftp;

/// <summary>Un fichier ou dossier distant.</summary>
/// <param name="IsDirectory">Vrai pour un dossier, ou un lien symbolique qui pointe vers un dossier.</param>
/// <param name="Permissions">Bits rwx (<see cref="UnixPermissions"/>).</param>
public sealed record RemoteEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    bool IsSymbolicLink,
    long Size,
    DateTime LastWriteTime,
    int Permissions);

/// <summary>Taille et date de modification (UTC, à la seconde comme en SFTP) d'un fichier distant.</summary>
public readonly record struct RemoteFileStamp(long Size, DateTime LastWriteTimeUtc)
{
    /// <summary>Même contenu probable : même taille, même date à la seconde près (précision du protocole SFTP v3).</summary>
    public bool SameAs(RemoteFileStamp other) =>
        Size == other.Size && Math.Abs((LastWriteTimeUtc - other.LastWriteTimeUtc).TotalSeconds) < 1;
}
