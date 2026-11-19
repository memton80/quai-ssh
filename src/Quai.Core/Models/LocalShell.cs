namespace Quai.Core.Models;

/// <summary>Un shell de cet ordinateur, lancé dans un terminal local.</summary>
/// <param name="Id">Identifiant stable, mémorisé dans les réglages (« pwsh », « cmd », « wsl:Ubuntu »…).</param>
/// <param name="Name">Nom affiché (titre de l'onglet, menus).</param>
/// <param name="Path">Exécutable lancé.</param>
/// <param name="Arguments">Arguments, passés tels quels (mis entre guillemets si besoin par la bibliothèque PTY).</param>
public sealed record LocalShell(string Id, string Name, string Path, IReadOnlyList<string> Arguments)
{
    /// <summary>Ligne de commande lisible (info-bulle de l'onglet).</summary>
    public string CommandLine => Arguments.Count == 0
        ? Path
        : $"{Path} {string.Join(' ', Arguments.Select(a => a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a))}";

    public bool Equals(LocalShell? other) =>
        other is not null
        && Id == other.Id
        && Name == other.Name
        && Path == other.Path
        && Arguments.SequenceEqual(other.Arguments);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Path, Arguments.Count);
}
