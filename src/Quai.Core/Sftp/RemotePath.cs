namespace Quai.Core.Sftp;

/// <summary>Chemins distants (POSIX, séparateur « / »), indépendants du système local.</summary>
public static class RemotePath
{
    public const string Root = "/";

    /// <summary>Résout « . », « .. » et les barres multiples ; le résultat est toujours absolu.</summary>
    public static string Normalize(string? path, string baseDirectory = Root)
    {
        var text = string.IsNullOrWhiteSpace(path) ? baseDirectory : path.Trim();
        if (!text.StartsWith('/'))
        {
            text = baseDirectory.TrimEnd('/') + "/" + text;
        }

        var parts = new List<string>();
        foreach (var part in text.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            parts.Add(part);
        }

        return Root + string.Join('/', parts);
    }

    public static string Combine(string directory, string name) =>
        directory.EndsWith('/') ? directory + name : directory + "/" + name;

    /// <summary>Dossier parent, ou <c>null</c> à la racine.</summary>
    public static string? GetParent(string path)
    {
        var normalized = Normalize(path);
        if (normalized == Root)
        {
            return null;
        }

        var index = normalized.LastIndexOf('/');
        return index <= 0 ? Root : normalized[..index];
    }

    public static string GetName(string path)
    {
        var normalized = Normalize(path);
        return normalized == Root ? Root : normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    /// <summary>Un nom de fichier acceptable : non vide, sans « / », ni « . » ni « .. ».</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && !name.Contains('/') && name != "." && name != ".." && !name.Contains('\0');
}
