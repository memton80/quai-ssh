namespace Quai.Core.Models;

/// <summary>Chemins de dossiers du carnet : niveaux séparés par « / ».</summary>
public static class FolderPath
{
    public const char Separator = '/';

    public static string Normalize(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return string.Empty;
        }

        var parts = folder
            .Replace('\\', Separator)
            .Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(Separator, parts);
    }

    /// <summary>Le dossier lui-même puis tous ses parents (« a/b/c » → « a », « a/b », « a/b/c »).</summary>
    public static IEnumerable<string> SelfAndAncestors(string folder)
    {
        var normalized = Normalize(folder);
        if (normalized.Length == 0)
        {
            yield break;
        }

        var index = -1;
        while ((index = normalized.IndexOf(Separator, index + 1)) >= 0)
        {
            yield return normalized[..index];
        }

        yield return normalized;
    }

    /// <summary>Vrai si <paramref name="folder"/> est <paramref name="ancestor"/> ou l'un de ses sous-dossiers.</summary>
    public static bool IsSameOrDescendant(string folder, string ancestor)
    {
        var f = Normalize(folder);
        var a = Normalize(ancestor);
        if (a.Length == 0)
        {
            return true;
        }

        return f.Equals(a, StringComparison.OrdinalIgnoreCase)
            || f.StartsWith(a + Separator, StringComparison.OrdinalIgnoreCase);
    }
}
