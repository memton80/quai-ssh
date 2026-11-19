using System.Text;
using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Storage;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.ViewModels;

public sealed class SnippetsTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly JsonSnippetRepository _snippets;
    private readonly JsonConnectionRepository _connections;
    private readonly FakeDialogService _dialogs = new();
    private readonly RecordingNavigator _navigator = new();
    private readonly ConnectionProfile _web = new() { Name = "Web", Host = "web.example.org", Username = "deploy" };
    private readonly ConnectionProfile _db = new() { Name = "Base", Host = "db.example.org", Username = "postgres" };

    public SnippetsTests()
    {
        _snippets = new JsonSnippetRepository(_temp.File("snippets.json"));
        _connections = new JsonConnectionRepository(_temp.File("connections.json"));
        _connections.Save(_web);
        _connections.Save(_db);
    }

    public void Dispose() => _temp.Dispose();

    private SnippetsViewModel CreateViewModel() => new(_snippets, _connections, _dialogs, new KeyStrings(), _navigator);

    // ---- Modèle ------------------------------------------------------------------------------

    [Theory]
    [InlineData("uptime", true, "uptime\r")]
    [InlineData("uptime", false, "uptime")]
    [InlineData("cd /var/log\r\ntail -f syslog\n", true, "cd /var/log\rtail -f syslog\r")]
    [InlineData("cd /var/log\ntail -f ", false, "cd /var/log\rtail -f ")]
    [InlineData("", true, "")]
    public void A_snippet_is_typed_line_by_line(string command, bool run, string expected)
    {
        Assert.Equal(expected, new Snippet { Command = command, Run = run }.ToTerminalInput());
    }

    [Fact]
    public void A_snippet_without_name_shows_its_first_line()
    {
        var snippet = new Snippet { Command = "\nsudo systemctl restart nginx\nsudo systemctl status nginx" };

        Assert.Equal("sudo systemctl restart nginx …", snippet.DisplayName);
    }

    [Fact]
    public void Snippets_are_stored_without_their_computed_fields_and_reloaded()
    {
        var snippet = new Snippet { Name = " Disque ", Command = "df -h\r\n", ConnectionId = _web.Id, Run = false };
        _snippets.Save(snippet);

        var json = File.ReadAllText(_temp.File("snippets.json"));
        var reloaded = Assert.Single(new JsonSnippetRepository(_temp.File("snippets.json")).GetAll());

        Assert.DoesNotContain("displayName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("firstLine", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(("Disque", "df -h", _web.Id, false), (reloaded.Name, reloaded.Command, reloaded.ConnectionId, reloaded.Run));
    }

    [Fact]
    public void An_unreadable_file_is_set_aside()
    {
        File.WriteAllText(_temp.File("snippets.json"), "{ pas du json");

        Assert.Empty(new JsonSnippetRepository(_temp.File("snippets.json")).GetAll());
        Assert.Contains(Directory.GetFiles(_temp.Path), f => Path.GetFileName(f).StartsWith("snippets.json.", StringComparison.Ordinal));
    }

    // ---- Page --------------------------------------------------------------------------------

    [Fact]
    public async Task Creating_a_snippet_for_one_connection()
    {
        SnippetEditorViewModel? form = null;
        _dialogs.OnEditSnippet = editor =>
        {
            form = editor;
            editor.Name = "Journal";
            editor.Command = "journalctl -f";
            editor.ScopeIndex = editor.Scopes.ToList().FindIndex(s => s.ConnectionId == _db.Id);
            return editor.Validate();
        };
        var vm = CreateViewModel();
        Assert.True(vm.IsEmpty);

        await vm.NewCommand.ExecuteAsync(null);

        Assert.NotNull(form);
        Assert.True(form.IsNew);
        Assert.Equal("Snippets_ScopeAll", form.Scopes[0].Label);
        var item = Assert.Single(vm.Items);
        Assert.Equal(("Journal", "journalctl -f", "Base"), (item.Name, item.Preview, item.Scope));
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void A_snippet_needs_a_command()
    {
        var editor = new SnippetEditorViewModel(new KeyStrings(), null, [_web]) { Name = "Vide", Command = "  " };

        Assert.False(editor.Validate());
        Assert.Equal("Snippets_CommandRequired", editor.ErrorMessage);
    }

    [Fact]
    public async Task Editing_keeps_the_identity_and_a_deleted_connection_stays_visible()
    {
        var orphan = new Snippet { Name = "Ancien", Command = "ls", ConnectionId = Guid.NewGuid() };
        _snippets.Save(orphan);
        var vm = CreateViewModel();
        Assert.Equal("Snippets_ScopeMissing", Assert.Single(vm.Items).Scope);
        _dialogs.OnEditSnippet = editor =>
        {
            Assert.Equal("Snippets_ScopeMissing", editor.Scopes[editor.ScopeIndex].Label);
            editor.ScopeIndex = 0;
            editor.Command = "ls -la";
            return true;
        };

        await vm.Items[0].EditCommand.ExecuteAsync(null);

        var saved = Assert.Single(_snippets.GetAll());
        Assert.Equal((orphan.Id, "ls -la", (Guid?)null), (saved.Id, saved.Command, saved.ConnectionId));
        Assert.Equal("Snippets_ScopeAll", Assert.Single(vm.Items).Scope);
    }

    [Fact]
    public async Task Deleting_asks_first()
    {
        _snippets.Save(new Snippet { Name = "À garder", Command = "true" });
        var vm = CreateViewModel();
        _dialogs.ConfirmResult = false;

        await vm.Items[0].DeleteCommand.ExecuteAsync(null);
        Assert.Single(_snippets.GetAll());

        _dialogs.ConfirmResult = true;
        await vm.Items[0].DeleteCommand.ExecuteAsync(null);

        Assert.Equal("Snippets_DeleteMessage(À garder)", _dialogs.Confirmations[^1]);
        Assert.Empty(_snippets.GetAll());
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void Search_looks_in_names_commands_and_scopes()
    {
        _snippets.Save(new Snippet { Name = "Disque", Command = "df -h" });
        _snippets.Save(new Snippet { Name = "Journal", Command = "journalctl -f", ConnectionId = _db.Id });
        var vm = CreateViewModel();

        vm.SearchText = "base";
        Assert.Equal("Journal", Assert.Single(vm.Items).Name);

        vm.SearchText = "DF";
        Assert.Equal("Disque", Assert.Single(vm.Items).Name);

        vm.SearchText = "rien";
        Assert.True(vm.HasNoResults);
    }

    // ---- Envoi dans un terminal --------------------------------------------------------------

    // ---- Raccourcis ---------------------------------------------------------------------------

    private static readonly KeyGesture CtrlShift1 = new(Control: true, Shift: true, Alt: false, Key: '1');

    [Fact]
    public async Task A_shortcut_sends_its_snippet_and_the_connection_specific_one_wins()
    {
        _snippets.Save(new Snippet { Name = "Partout", Command = "uptime", Shortcut = "Ctrl+Shift+1" });
        _snippets.Save(new Snippet { Name = "Web", Command = "systemctl reload nginx", ConnectionId = _web.Id, Shortcut = "Ctrl+Shift+2" });
        _snippets.Save(new Snippet { Name = "Base", Command = "psql", ConnectionId = _db.Id, Shortcut = "Ctrl+Shift+2" });
        var (tab, session) = await TerminalOnAsync(_web);
        var page = CreateViewModel();

        Assert.True(page.TryRunShortcut(tab, CtrlShift1));
        Assert.True(page.TryRunShortcut(tab, CtrlShift1 with { Key = '2' }));
        Assert.False(page.TryRunShortcut(tab, CtrlShift1 with { Key = '3' }));

        Assert.Equal(["uptime\r", "systemctl reload nginx\r"], session.Sent.Select(b => Encoding.UTF8.GetString(b)));
    }

    [Fact]
    public async Task A_shortcut_does_nothing_in_a_closed_tab_or_for_a_forbidden_combination()
    {
        _snippets.Save(new Snippet { Command = "uptime", Shortcut = "Ctrl+Shift+1" });
        var (tab, session) = await TerminalOnAsync(_web);
        var page = CreateViewModel();

        Assert.False(page.TryRunShortcut(tab, new KeyGesture(true, false, false, 'C')));
        session.Close();
        Assert.False(page.TryRunShortcut(tab, CtrlShift1));
        Assert.False(page.TryRunShortcut(null, CtrlShift1));
        Assert.Empty(session.Sent);
    }

    [Fact]
    public void The_editor_explains_a_refused_combination_and_keeps_the_dialog_open()
    {
        var editor = new SnippetEditorViewModel(new KeyStrings(), null, [_web]) { Command = "uptime" };

        editor.CaptureShortcut(control: true, shift: false, alt: false, key: 'R');

        Assert.Equal("Snippets_ShortcutShellKey(Key_Ctrl+R)", editor.ShortcutError);
        Assert.False(editor.Validate());

        editor.CaptureShortcut(control: false, shift: false, alt: false, key: 0x0D);
        Assert.Equal("Snippets_ShortcutUnsupported", editor.ShortcutError);
        Assert.Equal("Key_Ctrl+R", editor.ShortcutText);

        editor.CaptureShortcut(control: true, shift: true, alt: false, key: '1');
        Assert.True(editor.Validate());
        Assert.Equal("Key_Ctrl+Key_Shift+1", editor.ShortcutText);
        Assert.Equal("Ctrl+Shift+1", editor.BuildSnippet().Shortcut);

        editor.ClearShortcut();
        Assert.Null(editor.BuildSnippet().Shortcut);
    }

    [Fact]
    public void A_shortcut_already_used_where_the_snippet_applies_is_refused_but_free_for_another_connection()
    {
        var existing = new Snippet { Name = "Recharger", Command = "nginx -s reload", ConnectionId = _web.Id, Shortcut = "Ctrl+Shift+1" };
        var editor = new SnippetEditorViewModel(new KeyStrings(), null, [_web, _db], [existing]) { Command = "psql" };

        editor.CaptureShortcut(control: true, shift: true, alt: false, key: '1');
        Assert.Equal("Snippets_ShortcutTaken(Key_Ctrl+Key_Shift+1,Recharger)", editor.ShortcutError);

        editor.ScopeIndex = editor.Scopes.ToList().FindIndex(s => s.ConnectionId == _db.Id);
        Assert.Empty(editor.ShortcutError);
        Assert.True(editor.Validate());

        // Le snippet modifié ne se gêne pas lui-même.
        var self = new SnippetEditorViewModel(new KeyStrings(), existing, [_web, _db], [existing]);
        Assert.True(self.Validate());
        Assert.Equal("Key_Ctrl+Key_Shift+1", self.ShortcutText);
    }

    [Fact]
    public void The_page_shows_each_shortcut_and_it_survives_a_reload()
    {
        _snippets.Save(new Snippet { Name = "Disque", Command = "df -h", Shortcut = "Alt+Shift+F5" });

        var item = Assert.Single(CreateViewModel().Items);
        Assert.Equal("Key_Shift+Key_Alt+F5", item.ShortcutText);
        Assert.True(item.HasShortcut);
        Assert.Equal("Shift+Alt+F5", new JsonSnippetRepository(_temp.File("snippets.json")).GetAll().Single().Shortcut);
    }

    private async Task<(TerminalTabViewModel Tab, FakeTerminalSession Session)> TerminalOnAsync(ConnectionProfile profile)
    {
        var factory = new FakeSessionFactory();
        var session = new FakeTerminalSession();
        factory.Terminals.Enqueue(() => session);
        var tab = new TerminalTabViewModel(profile.Clone(), factory, new KeyStrings(), new ImmediateDispatcher());
        await tab.ConnectAsync(80, 24);
        return (tab, session);
    }

    [Fact]
    public async Task The_picker_offers_the_connection_snippets_first_then_global_ones_and_sends_the_choice()
    {
        _snippets.Save(new Snippet { Name = "Zéro", Command = "uptime" });
        _snippets.Save(new Snippet { Name = "Web seulement", Command = "nginx -t", ConnectionId = _web.Id });
        _snippets.Save(new Snippet { Name = "Base seulement", Command = "psql", ConnectionId = _db.Id });
        var (tab, session) = await TerminalOnAsync(_web);
        SnippetPickerViewModel? shown = null;
        _dialogs.OnPickSnippet = picker =>
        {
            shown = picker;
            return SnippetPickerResult.Send;
        };

        Assert.True(await CreateViewModel().RunInAsync(tab));

        Assert.NotNull(shown);
        Assert.Equal(["Web seulement", "Zéro"], shown.Matches.Select(s => s.Name));
        Assert.Equal("Web", shown.Target);
        Assert.Equal("nginx -t\r", Encoding.UTF8.GetString(Assert.Single(session.Sent)));
    }

    [Fact]
    public void Filtering_keeps_a_selection_and_arrows_move_it()
    {
        _snippets.Save(new Snippet { Name = "Disque", Command = "df -h" });
        _snippets.Save(new Snippet { Name = "Mémoire", Command = "free -h" });
        _snippets.Save(new Snippet { Name = "Processus", Command = "ps aux --sort=-%mem | head" });
        var picker = new SnippetPickerViewModel(_snippets.GetAll(), _web.Id, "Web");

        Assert.Equal("Disque", picker.Selected?.Name);
        picker.Move(1);
        Assert.Equal("Mémoire", picker.Selected?.Name);
        picker.Move(5);
        Assert.Equal("Processus", picker.Selected?.Name);

        picker.Filter = "-h mém";
        Assert.Equal("Mémoire", Assert.Single(picker.Matches).Name);
        Assert.Equal("Mémoire", picker.Selected?.Name);

        picker.Filter = "introuvable";
        Assert.True(picker.HasNoMatch);
        Assert.Null(picker.Selected);
        Assert.False(picker.HasNone);
    }

    [Fact]
    public async Task Cancelling_sends_nothing_and_manage_opens_the_page()
    {
        _snippets.Save(new Snippet { Name = "Disque", Command = "df -h" });
        var (tab, session) = await TerminalOnAsync(_web);
        var vm = CreateViewModel();

        _dialogs.OnPickSnippet = _ => SnippetPickerResult.Cancelled;
        Assert.False(await vm.RunInAsync(tab));

        _dialogs.OnPickSnippet = _ => SnippetPickerResult.Manage;
        Assert.False(await vm.RunInAsync(tab));

        Assert.Empty(session.Sent);
        Assert.Equal([AppPage.Snippets], _navigator.Pages);
    }

    [Fact]
    public async Task Nothing_is_offered_to_a_disconnected_tab()
    {
        var (tab, session) = await TerminalOnAsync(_web);
        session.Close();
        var asked = false;
        _dialogs.OnPickSnippet = _ =>
        {
            asked = true;
            return SnippetPickerResult.Send;
        };

        Assert.False(await CreateViewModel().RunInAsync(tab));
        Assert.False(asked);
    }
}
