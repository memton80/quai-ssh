using CommunityToolkit.Mvvm.ComponentModel;
using Quai.Core.Localization;
using Quai.Core.Models;

namespace Quai.Core.ViewModels;

/// <summary>Portée proposée dans la fiche d'un snippet : toutes les connexions, ou une connexion du carnet.</summary>
public sealed record SnippetScope(Guid? ConnectionId, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Fiche de création / modification d'un snippet.</summary>
public sealed partial class SnippetEditorViewModel : ObservableObject
{
    private readonly IStrings _strings;
    private readonly Snippet _original;

    public SnippetEditorViewModel(IStrings strings, Snippet? existing, IEnumerable<ConnectionProfile> connections)
    {
        _strings = strings;
        IsNew = existing is null;
        _original = existing?.Clone() ?? new Snippet();

        List<SnippetScope> scopes = [new(null, strings.Get("Snippets_ScopeAll"))];
        scopes.AddRange(connections.Select(c => new SnippetScope(c.Id, c.DisplayName)));
        if (_original.ConnectionId is { } id && scopes.All(s => s.ConnectionId != id))
        {
            // Connexion supprimée depuis : la portée reste visible (et modifiable).
            scopes.Add(new SnippetScope(id, strings.Get("Snippets_ScopeMissing")));
        }

        Scopes = scopes;
        ScopeIndex = Math.Max(0, scopes.FindIndex(s => s.ConnectionId == _original.ConnectionId));
        Name = _original.Name;
        Command = _original.Command;
        Run = _original.Run;
        ErrorMessage = string.Empty;
    }

    public bool IsNew { get; }

    public string Title => _strings.Get(IsNew ? "Snippets_TitleNew" : "Snippets_TitleEdit");

    public IReadOnlyList<SnippetScope> Scopes { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Command { get; set; }

    [ObservableProperty]
    public partial int ScopeIndex { get; set; }

    /// <summary>Entrée après la dernière ligne.</summary>
    [ObservableProperty]
    public partial bool Run { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; }

    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>Vérifie la fiche ; en cas d'erreur, <see cref="ErrorMessage"/> l'explique.</summary>
    public bool Validate()
    {
        ErrorMessage = string.IsNullOrWhiteSpace(Command) ? _strings.Get("Snippets_CommandRequired") : string.Empty;
        return !HasError;
    }

    public Snippet BuildSnippet()
    {
        var snippet = _original.Clone();
        snippet.Name = Name ?? string.Empty;
        snippet.Command = Command ?? string.Empty;
        snippet.Run = Run;
        snippet.ConnectionId = ScopeIndex >= 0 && ScopeIndex < Scopes.Count ? Scopes[ScopeIndex].ConnectionId : null;
        snippet.Normalize();
        return snippet;
    }
}
