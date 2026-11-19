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
    private readonly IReadOnlyList<Snippet> _others;

    /// <param name="snippets">Snippets enregistrés, pour refuser un raccourci déjà pris.</param>
    public SnippetEditorViewModel(
        IStrings strings,
        Snippet? existing,
        IEnumerable<ConnectionProfile> connections,
        IEnumerable<Snippet>? snippets = null)
    {
        _strings = strings;
        _others = [.. (snippets ?? []).Where(s => existing is null || s.Id != existing.Id)];
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
        Gesture = _original.Gesture;
        ShortcutError = string.Empty;
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

    /// <summary>Raccourci saisi (peut être refusé : voir <see cref="ShortcutError"/>) ; <c>null</c> = aucun.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutText))]
    [NotifyPropertyChangedFor(nameof(HasShortcut))]
    public partial KeyGesture? Gesture { get; set; }

    public bool HasShortcut => Gesture is not null;

    /// <summary>Raccourci affiché (« Ctrl+Maj+1 »), vide s'il n'y en a pas.</summary>
    public string ShortcutText => Gesture is { } gesture ? FormatGesture(_strings, gesture) : string.Empty;

    /// <summary>Pourquoi le raccourci saisi est refusé ; vide s'il est accepté.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShortcutError))]
    public partial string ShortcutError { get; set; }

    public bool HasShortcutError => ShortcutError.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; }

    /// <summary>Raccourci affiché avec les noms de touches de l'interface.</summary>
    public static string FormatGesture(IStrings strings, KeyGesture gesture) =>
        gesture.Format(strings.Get("Key_Ctrl"), strings.Get("Key_Shift"), strings.Get("Key_Alt"));

    /// <summary>
    /// Combinaison appuyée dans le champ du raccourci : retenue, et expliquée si elle est refusée. Une touche seule
    /// non prise en charge (Échap, Entrée…) n'efface pas le raccourci précédent.
    /// </summary>
    public void CaptureShortcut(bool control, bool shift, bool alt, int key)
    {
        var gesture = new KeyGesture(control, shift, alt, key);
        if (gesture.Problem == KeyGestureProblem.UnsupportedKey)
        {
            ShortcutError = _strings.Get("Snippets_ShortcutUnsupported");
            return;
        }

        Gesture = gesture;
        ShortcutError = ExplainShortcut();
    }

    public void ClearShortcut()
    {
        Gesture = null;
        ShortcutError = string.Empty;
    }

    partial void OnScopeIndexChanged(int value)
    {
        // Un raccourci libre pour une connexion peut être pris pour une autre portée.
        if (Gesture is not null)
        {
            ShortcutError = ExplainShortcut();
        }
    }

    private string ExplainShortcut()
    {
        if (Gesture is not { } gesture)
        {
            return string.Empty;
        }

        var text = FormatGesture(_strings, gesture);
        switch (gesture.Problem)
        {
            case KeyGestureProblem.NeedsModifier:
                return _strings.Format("Snippets_ShortcutNeedsModifier", text);
            case KeyGestureProblem.AltGr:
                return _strings.Format("Snippets_ShortcutAltGr", text);
            case KeyGestureProblem.ShellKey:
                return _strings.Format("Snippets_ShortcutShellKey", text);
            case KeyGestureProblem.Reserved:
                return _strings.Format("Snippets_ShortcutReserved", text);
            case KeyGestureProblem.UnsupportedKey:
                return _strings.Get("Snippets_ShortcutUnsupported");
        }

        var candidate = new Snippet { ConnectionId = SelectedConnectionId };
        var taken = _others.FirstOrDefault(s => s.Gesture == gesture && s.OverlapsWith(candidate));
        return taken is null ? string.Empty : _strings.Format("Snippets_ShortcutTaken", text, taken.DisplayName);
    }

    private Guid? SelectedConnectionId => ScopeIndex >= 0 && ScopeIndex < Scopes.Count ? Scopes[ScopeIndex].ConnectionId : null;

    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>Vérifie la fiche ; en cas d'erreur, <see cref="ErrorMessage"/> l'explique.</summary>
    public bool Validate()
    {
        ShortcutError = ExplainShortcut();
        ErrorMessage = string.IsNullOrWhiteSpace(Command) ? _strings.Get("Snippets_CommandRequired") : ShortcutError;
        return !HasError;
    }

    public Snippet BuildSnippet()
    {
        var snippet = _original.Clone();
        snippet.Name = Name ?? string.Empty;
        snippet.Command = Command ?? string.Empty;
        snippet.Run = Run;
        snippet.ConnectionId = SelectedConnectionId;
        snippet.Shortcut = Gesture?.ToString();
        snippet.Normalize();
        return snippet;
    }
}
