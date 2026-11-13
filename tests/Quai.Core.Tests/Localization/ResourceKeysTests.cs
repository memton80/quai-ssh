using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Quai.Core.Tests.Localization;

/// <summary>
/// Garde-fou : toute clé de chaîne utilisée dans le code ou le XAML doit exister dans le <c>.resw</c> français
/// (une clé absente s'afficherait telle quelle à l'écran).
/// </summary>
public partial class ResourceKeysTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Every_key_used_in_code_or_xaml_exists_in_the_french_resources()
    {
        var defined = LoadKeys();
        var missing = new SortedSet<string>();

        // Dans le C#, toute chaîne littérale « Préfixe_Nom » dont le préfixe est celui d'une famille de
        // ressources (Common_, Editor_, …) est considérée comme une clé.
        var prefixes = defined.Select(k => k[..(k.IndexOf('_') + 1)]).ToHashSet();
        foreach (var file in SourceFiles("*.cs"))
        {
            foreach (Match match in CodeKey().Matches(File.ReadAllText(file)))
            {
                var key = match.Groups[1].Value;
                if (prefixes.Contains(key[..(key.IndexOf('_') + 1)]) && !defined.Contains(key))
                {
                    missing.Add($"{key} ({Path.GetFileName(file)})");
                }
            }
        }

        foreach (var file in SourceFiles("*.xaml"))
        {
            foreach (Match match in XamlKey().Matches(File.ReadAllText(file)))
            {
                if (!defined.Contains(match.Groups[1].Value))
                {
                    missing.Add($"{match.Groups[1].Value} ({Path.GetFileName(file)})");
                }
            }
        }

        Assert.True(missing.Count == 0, "Clés absentes de Resources.resw : " + string.Join(", ", missing));
    }

    [Fact]
    public void Resource_keys_are_unique_and_values_are_not_empty()
    {
        var entries = XDocument.Load(ResourceFile()).Root!.Elements("data").ToList();

        var duplicates = entries.GroupBy(e => (string)e.Attribute("name")!).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(duplicates);
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace((string?)e.Element("value"))));
    }

    private static HashSet<string> LoadKeys() =>
        [.. XDocument.Load(ResourceFile()).Root!.Elements("data").Select(e => (string)e.Attribute("name")!)];

    private static string ResourceFile() =>
        Path.Combine(Root, "src", "Quai.App", "Strings", "fr-FR", "Resources.resw");

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Quai.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Quai.sln introuvable.");
    }

    // "Famille_Nom"
    [GeneratedRegex(@"""([A-Z][A-Za-z0-9]*_[A-Za-z0-9_]+)""")]
    private static partial Regex CodeKey();

    // {l:Str Key=Clé}
    [GeneratedRegex(@"\{l:Str\s+(?:Key=)?([A-Za-z0-9_]+)\}")]
    private static partial Regex XamlKey();
}
