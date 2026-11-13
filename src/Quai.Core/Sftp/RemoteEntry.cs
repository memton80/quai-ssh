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
