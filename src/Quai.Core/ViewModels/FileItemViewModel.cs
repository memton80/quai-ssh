using System.Globalization;
using Quai.Core.Services;
using Quai.Core.Sftp;

namespace Quai.Core.ViewModels;

/// <summary>Une ligne d'un panneau de l'explorateur (fichier, dossier, ou « .. » pour remonter).</summary>
public sealed class FileItemViewModel
{
    private FileItemViewModel(string name, string fullPath, bool isDirectory)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        SizeText = string.Empty;
        ModifiedText = string.Empty;
        PermissionsText = string.Empty;
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    /// <summary>Ligne « .. » : remonte au dossier parent, n'est jamais transférée ni supprimée.</summary>
    public bool IsParent { get; private init; }

    public bool IsLink { get; private init; }

    public long Size { get; private init; }

    public string SizeText { get; private init; }

    public string ModifiedText { get; private init; }

    /// <summary>« rwxr-xr-x » (panneau distant uniquement).</summary>
    public string PermissionsText { get; private init; }

    /// <summary>Entrée distante d'origine (nulle pour un élément local ou « .. »).</summary>
    public RemoteEntry? Remote { get; private init; }

    /// <summary>Glyphe Segoe Fluent Icons : remonter, dossier, lien ou document.</summary>
    public string Glyph => IsParent ? "" : IsLink ? "" : IsDirectory ? "" : "";

    public static FileItemViewModel Parent(string parentPath) => new("..", parentPath, true) { IsParent = true };

    public static FileItemViewModel FromLocal(LocalEntry entry, FileSizeFormatter sizes) =>
        new(entry.Name, entry.FullPath, entry.IsDirectory)
        {
            Size = entry.Size,
            SizeText = entry.IsDirectory ? string.Empty : sizes.Format(entry.Size),
            ModifiedText = FormatDate(entry.LastWriteTime),
        };

    public static FileItemViewModel FromRemote(RemoteEntry entry, FileSizeFormatter sizes) =>
        new(entry.Name, entry.FullPath, entry.IsDirectory)
        {
            IsLink = entry.IsSymbolicLink,
            Size = entry.Size,
            SizeText = entry.IsDirectory ? string.Empty : sizes.Format(entry.Size),
            ModifiedText = FormatDate(entry.LastWriteTime),
            PermissionsText = UnixPermissions.ToSymbolic(entry.Permissions, entry.IsDirectory, entry.IsSymbolicLink),
            Remote = entry,
        };

    private static string FormatDate(DateTime value) =>
        value == default ? string.Empty : value.ToString("g", CultureInfo.CurrentCulture);
}
