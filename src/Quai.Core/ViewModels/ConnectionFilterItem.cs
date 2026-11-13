namespace Quai.Core.ViewModels;

public enum ConnectionFilterKind
{
    All,
    Folder,
    Tag,
}

/// <summary>Une entrée du volet de filtres du carnet : tout, un dossier ou un tag.</summary>
public sealed record ConnectionFilterItem(ConnectionFilterKind Kind, string Value, string Label, int Count)
{
    /// <summary>Glyphe Segoe Fluent Icons associé (bibliothèque, dossier, étiquette).</summary>
    public string Glyph => Kind switch
    {
        ConnectionFilterKind.Folder => "",
        ConnectionFilterKind.Tag => "",
        _ => "",
    };

    /// <summary>Retrait visuel des sous-dossiers.</summary>
    public int Depth => Kind == ConnectionFilterKind.Folder ? Value.Count(c => c == Models.FolderPath.Separator) : 0;

    public string CountText => Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public bool IsSameFilter(ConnectionFilterItem? other) =>
        other is not null && other.Kind == Kind && string.Equals(other.Value, Value, StringComparison.OrdinalIgnoreCase);
}
