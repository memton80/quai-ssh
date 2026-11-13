namespace Quai.Core.Models;

/// <summary>Saisie et nettoyage des tags (séparés par des virgules ou des points-virgules).</summary>
public static class TagList
{
    private static readonly char[] Separators = [',', ';', '\n', '\r'];

    public static List<string> Parse(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : Normalize(text.Split(Separators));

    public static List<string> Normalize(IEnumerable<string>? tags)
    {
        var result = new List<string>();
        if (tags is null)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in tags)
        {
            var tag = raw?.Trim().TrimStart('#').Trim();
            if (!string.IsNullOrEmpty(tag) && seen.Add(tag))
            {
                result.Add(tag);
            }
        }

        return result;
    }

    public static string Format(IEnumerable<string> tags) => string.Join(", ", tags);
}
