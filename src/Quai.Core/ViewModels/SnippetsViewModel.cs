using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quai.Core.Localization;
using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Storage;

namespace Quai.Core.ViewModels;

/// <summary>Une ligne de la page des snippets.</summary>
public sealed class SnippetItemViewModel
{
    public SnippetItemViewModel(Snippet snippet, string scope, SnippetsViewModel owner)
    {
        Snippet = snippet;
        Scope = scope;
        EditCommand = new AsyncRelayCommand(() => owner.EditAsync(this));
        DeleteCommand = new AsyncRelayCommand(() => owner.DeleteAsync(this));
    }

    public Snippet Snippet { get; }

    public string Name => Snippet.DisplayName;

    public string Preview => Snippet.FirstLine;

    /// <summary>Raccourci affiché (« Ctrl+Maj+1 »), vide s'il n'y en a pas.</summary>
    public string ShortcutText { get; init; } = string.Empty;

    public bool HasShortcut => ShortcutText.Length > 0;

    /// <summary>« Toutes les connexions » ou le nom de la connexion réservée.</summary>
    public string Scope { get; }

    public bool IsGlobal => Snippet.ConnectionId is null;

    public IAsyncRelayCommand EditCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }
}

/// <summary>Page des snippets (création, modification, suppression) et envoi d'un snippet dans un terminal.</summary>
public sealed partial class SnippetsViewModel : ObservableObject
{
    private readonly ISnippetRepository _snippets;
    private readonly IConnectionRepository _connections;
    private readonly IDialogService _dialogs;
    private readonly IStrings _strings;
    private readonly IShellNavigator? _navigator;

    public SnippetsViewModel(
        ISnippetRepository snippets,
        IConnectionRepository connections,
        IDialogService dialogs,
        IStrings strings,
        IShellNavigator? navigator = null)
    {
        _snippets = snippets;
        _connections = connections;
        _dialogs = dialogs;
        _strings = strings;
        _navigator = navigator;
        SearchText = string.Empty;

        // Un snippet ajouté ou une connexion renommée ailleurs : la page reste à jour.
        _snippets.Changed += (_, _) => Reload();
        _connections.Changed += (_, _) => Reload();
        Reload();
    }

    public ObservableCollection<SnippetItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasNoResults { get; set; }

    public void Reload()
    {
        var names = _connections.GetAll().ToDictionary(c => c.Id, c => c.DisplayName);
        var all = _snippets.GetAll();
        var words = (SearchText ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Items.Clear();
        foreach (var snippet in all)
        {
            var scope = snippet.ConnectionId is not { } id
                ? _strings.Get("Snippets_ScopeAll")
                : names.TryGetValue(id, out var name) ? name : _strings.Get("Snippets_ScopeMissing");
            if (words.All(w => snippet.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                || snippet.Command.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                || scope.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            {
                Items.Add(new SnippetItemViewModel(snippet, scope, this)
                {
                    ShortcutText = snippet.Gesture is { } gesture ? SnippetEditorViewModel.FormatGesture(_strings, gesture) : string.Empty,
                });
            }
        }

        IsEmpty = all.Count == 0;
        HasNoResults = all.Count > 0 && Items.Count == 0;
    }

    [RelayCommand]
    public async Task NewAsync()
    {
        var editor = new SnippetEditorViewModel(_strings, null, _connections.GetAll(), _snippets.GetAll());
        if (await _dialogs.EditSnippetAsync(editor))
        {
            _snippets.Save(editor.BuildSnippet());
        }
    }

    public async Task EditAsync(SnippetItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var editor = new SnippetEditorViewModel(_strings, item.Snippet, _connections.GetAll(), _snippets.GetAll());
        if (await _dialogs.EditSnippetAsync(editor))
        {
            _snippets.Save(editor.BuildSnippet());
        }
    }

    public async Task DeleteAsync(SnippetItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            _strings.Get("Snippets_DeleteTitle"),
            _strings.Format("Snippets_DeleteMessage", item.Name),
            _strings.Get("Common_Delete"),
            isDestructive: true);
        if (confirmed)
        {
            _snippets.Delete(item.Snippet.Id);
        }
    }

    /// <summary>
    /// <c>Ctrl+Maj+P</c> dans un terminal : choix d'un snippet (ceux de la connexion et ceux de toutes), envoyé
    /// comme s'il était tapé. Retourne vrai si un snippet a été envoyé.
    /// </summary>
    public async Task<bool> RunInAsync(TerminalTabViewModel? tab)
    {
        if (tab is not { IsConnected: true })
        {
            return false;
        }

        var picker = new SnippetPickerViewModel(_snippets.GetAll(), tab.Profile.Id, tab.Title);
        var choice = await _dialogs.PickSnippetAsync(picker);
        if (choice == SnippetPickerResult.Manage)
        {
            _navigator?.NavigateTo(AppPage.Snippets);
            return false;
        }

        if (choice != SnippetPickerResult.Send || picker.Selected is not { } snippet || !tab.IsConnected)
        {
            return false;
        }

        tab.SendInput(snippet.ToTerminalInput());
        return true;
    }

    /// <summary>
    /// Combinaison appuyée dans un terminal : envoie le snippet qui porte ce raccourci et vaut pour l'onglet (celui
    /// réservé à sa connexion d'abord). Vrai si un snippet a été envoyé : la touche ne va alors pas au shell.
    /// </summary>
    public bool TryRunShortcut(TerminalTabViewModel? tab, KeyGesture gesture)
    {
        if (tab is not { IsConnected: true } || !gesture.IsValid)
        {
            return false;
        }

        var snippet = _snippets.GetAll()
            .Where(s => s.AppliesTo(tab.Profile.Id) && s.Gesture == gesture)
            .OrderBy(s => s.ConnectionId is null)
            .FirstOrDefault();
        if (snippet is null)
        {
            return false;
        }

        tab.SendInput(snippet.ToTerminalInput());
        return true;
    }

    partial void OnSearchTextChanged(string value) => Reload();
}
