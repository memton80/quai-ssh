namespace Quai.Core.Sftp;

/// <summary>Un fichier ou dossier de l'ordinateur local.</summary>
public sealed record LocalEntry(string Name, string FullPath, bool IsDirectory, long Size, DateTime LastWriteTime);

/// <summary>Parcours du système de fichiers local pour le panneau de gauche de l'explorateur.</summary>
public static class LocalFileSystem
{
    /// <summary>Chemin spécial : la liste des lecteurs (« Ce PC »).</summary>
    public const string Computer = "";

    public static string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Contenu d'un dossier (ou liste des lecteurs pour <see cref="Computer"/>), dossiers d'abord.</summary>
    public static IReadOnlyList<LocalEntry> List(string path)
    {
        if (path == Computer)
        {
            return [.. DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new LocalEntry(d.Name, d.RootDirectory.FullName, true, 0, default))];
        }

        var directory = new DirectoryInfo(path);
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            // Fichiers protégés du système (pagefile.sys, $Recycle.Bin…) : inutiles ici.
            AttributesToSkip = FileAttributes.System,
        };

        var entries = new List<LocalEntry>();
        foreach (var info in directory.EnumerateFileSystemInfos("*", options))
        {
            var isDirectory = info.Attributes.HasFlag(FileAttributes.Directory);
            entries.Add(new LocalEntry(
                info.Name,
                info.FullName,
                isDirectory,
                isDirectory ? 0 : ((FileInfo)info).Length,
                info.LastWriteTime));
        }

        return [.. entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Dossier parent ; la racine d'un lecteur remonte à la liste des lecteurs, qui n'a pas de parent.</summary>
    public static string? GetParent(string path)
    {
        if (path == Computer)
        {
            return null;
        }

        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
        if (parent is not null)
        {
            return parent;
        }

        // Racine : sous Windows on remonte à « Ce PC » ; sous Unix « / » est le sommet.
        return OperatingSystem.IsWindows() ? Computer : null;
    }

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name is not ("." or "..");
}
