using System.Globalization;
using Quai.Core.Models;

namespace Quai.Core.Services;

/// <summary>Recherche et filtrage du carnet de connexions.</summary>
public static class ConnectionFilter
{
    private const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private static readonly CompareInfo Comparer = CultureInfo.InvariantCulture.CompareInfo;

    /// <summary>
    /// Filtre par dossier (sous-dossiers inclus), par tag, et par texte libre : chaque mot de la recherche
    /// doit apparaître dans le nom, l'hôte, l'utilisateur, le port série, le dossier, un tag ou les notes
    /// (sans tenir compte de la casse ni des accents).
    /// </summary>
    public static IEnumerable<ConnectionProfile> Apply(
        IEnumerable<ConnectionProfile> source,
        string? query = null,
        string? folder = null,
        string? tag = null)
    {
        var terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var profile in source)
        {
            if (!string.IsNullOrEmpty(folder) && !FolderPath.IsSameOrDescendant(profile.Folder, folder))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(tag) && !profile.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (terms.All(term => Matches(profile, term)))
            {
                yield return profile;
            }
        }
    }

    private static bool Matches(ConnectionProfile profile, string term) =>
        Contains(profile.Name, term)
        || Contains(profile.Host, term)
        || Contains(profile.Username, term)
        || Contains(profile.Serial?.PortName, term)
        || Contains(profile.Folder, term)
        || Contains(profile.Notes, term)
        || profile.Tags.Any(t => Contains(t, term));

    private static bool Contains(string? text, string term) =>
        !string.IsNullOrEmpty(text) && Comparer.IndexOf(text, term, Options) >= 0;
}
