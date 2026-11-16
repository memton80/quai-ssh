using CommunityToolkit.Mvvm.ComponentModel;

namespace Quai.Core.ViewModels;

public enum ConnectionFilterKind
{
    All,
    Folder,
    Tag,
}

/// <summary>Une entrée du volet de filtres du carnet : tout, un dossier ou un tag.</summary>
public sealed partial class ConnectionFilterItem : ObservableObject
{
    public ConnectionFilterItem(ConnectionFilterKind kind, string value, string label, int count, bool hasChildren = false)
    {
        Kind = kind;
        Value = value;
        Label = label;
        Count = count;
        HasChildren = hasChildren;
        IsExpanded = true;
    }

    public ConnectionFilterKind Kind { get; }

    /// <summary>Chemin du dossier (« Prod/Web »), nom du tag, ou vide pour « toutes ».</summary>
    public string Value { get; }

    public string Label { get; }

    public int Count { get; }

    /// <summary>Dossier qui contient des sous-dossiers : il peut être replié.</summary>
    public bool HasChildren { get; }

    /// <summary>Sous-dossiers affichés (sans effet pour un dossier sans sous-dossier).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChevronGlyph))]
    public partial bool IsExpanded { get; set; }

    /// <summary>Glyphe Segoe Fluent Icons associé (bibliothèque, dossier, étiquette).</summary>
    public string Glyph => Kind switch
    {
        ConnectionFilterKind.Folder => "",
        ConnectionFilterKind.Tag => "",
        _ => "",
    };

    /// <summary>Chevron du bouton replier / déplier (vers le bas : déplié, vers la droite : replié).</summary>
    public string ChevronGlyph => IsExpanded ? "" : "";

    /// <summary>Retrait visuel des sous-dossiers.</summary>
    public int Depth => Kind == ConnectionFilterKind.Folder ? Value.Count(c => c == Models.FolderPath.Separator) : 0;

    public string CountText => Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public bool IsSameFilter(ConnectionFilterItem? other) =>
        other is not null && other.Kind == Kind && string.Equals(other.Value, Value, StringComparison.OrdinalIgnoreCase);

    /// <summary>Ce dossier est rangé (directement ou non) dans le dossier <paramref name="folder"/>.</summary>
    public bool IsInside(string folder) =>
        Kind == ConnectionFilterKind.Folder
        && Value.Length > folder.Length
        && Value[folder.Length] == Models.FolderPath.Separator
        && Value.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
}
