using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Quai.Core.Models;

namespace Quai.Core.ViewModels;

/// <summary>Choix d'un snippet à envoyer dans un terminal (<c>Ctrl+Maj+P</c>) : filtre par nom ou par commande.</summary>
public sealed partial class SnippetPickerViewModel : ObservableObject
{
    private readonly IReadOnlyList<Snippet> _all;

    /// <param name="connectionId">Connexion de l'onglet : seuls ses snippets et ceux de toutes les connexions sont proposés.</param>
    public SnippetPickerViewModel(IEnumerable<Snippet> snippets, Guid connectionId, string target)
    {
        // Ceux de la connexion d'abord : ce sont les plus spécifiques.
        _all = [.. snippets
            .Where(s => s.AppliesTo(connectionId))
            .OrderBy(s => s.ConnectionId is null)
            .ThenBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
        Target = target;
        Filter = string.Empty;
        Apply();
    }

    /// <summary>Onglet visé (affiché dans le titre).</summary>
    public string Target { get; }

    public ObservableCollection<Snippet> Matches { get; } = [];

    [ObservableProperty]
    public partial string Filter { get; set; }

    [ObservableProperty]
    public partial Snippet? Selected { get; set; }

    /// <summary>Aucun snippet pour cette connexion (rien à filtrer).</summary>
    public bool HasNone => _all.Count == 0;

    [ObservableProperty]
    public partial bool HasNoMatch { get; set; }

    /// <summary>Flèches haut / bas depuis le champ de recherche.</summary>
    public void Move(int delta)
    {
        if (Matches.Count == 0)
        {
            return;
        }

        var index = Selected is null ? -1 : Matches.IndexOf(Selected);
        Selected = Matches[Math.Clamp(index + delta, 0, Matches.Count - 1)];
    }

    partial void OnFilterChanged(string value) => Apply();

    private void Apply()
    {
        var words = (Filter ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Matches.Clear();
        foreach (var snippet in _all.Where(s => words.All(w =>
            s.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase)
            || s.Command.Contains(w, StringComparison.CurrentCultureIgnoreCase))))
        {
            Matches.Add(snippet);
        }

        Selected = Matches.FirstOrDefault();
        HasNoMatch = !HasNone && Matches.Count == 0;
    }
}
