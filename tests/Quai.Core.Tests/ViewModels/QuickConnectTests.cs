using Quai.Core.Models;
using Quai.Core.Security;
using Quai.Core.Services;
using Quai.Core.Storage;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.ViewModels;

public sealed class QuickConnectTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly JsonConnectionRepository _repository;
    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly RecordingLauncher _launcher = new();

    public QuickConnectTests()
    {
        _repository = new JsonConnectionRepository(_temp.File("connections.json"));
    }

    public void Dispose() => _temp.Dispose();

    private ConnectionsViewModel CreateViewModel() => new(_repository, _secrets, _dialogs, new KeyStrings(), _launcher);

    [Fact]
    public async Task An_address_opens_a_transient_terminal_without_touching_the_book()
    {
        var vm = CreateViewModel();
        vm.QuickConnectText = "alice@serveur:2222";

        await vm.QuickConnectCommand.ExecuteAsync(null);

        var opened = Assert.Single(_launcher.Terminals);
        Assert.True(opened.IsTransient);
        Assert.Equal("alice@serveur:2222", opened.Endpoint);
        Assert.Empty(_repository.GetAll());
        Assert.Equal(string.Empty, vm.QuickConnectText);
    }

    [Fact]
    public async Task A_missing_user_is_asked_and_cancelling_opens_nothing()
    {
        var vm = CreateViewModel();
        vm.QuickConnectText = "serveur";
        _dialogs.TextAnswers.Enqueue(null);

        await vm.QuickConnectCommand.ExecuteAsync(null);

        Assert.Equal("QuickConnect_UserTitle(serveur)", Assert.Single(_dialogs.TextRequests).Title);
        Assert.Empty(_launcher.Terminals);
        Assert.Equal("serveur", vm.QuickConnectText);

        _dialogs.TextAnswers.Enqueue(" bob ");
        await vm.QuickConnectCommand.ExecuteAsync(null);

        Assert.Equal("bob@serveur", Assert.Single(_launcher.Terminals).Endpoint);
    }

    [Fact]
    public async Task An_invalid_address_is_explained()
    {
        var vm = CreateViewModel();
        vm.QuickConnectText = "serveur:ssh";

        await vm.QuickConnectCommand.ExecuteAsync(null);

        Assert.Equal("QuickConnect_Invalid(serveur:ssh)", Assert.Single(_dialogs.Errors));
        Assert.Empty(_launcher.Terminals);
    }

    [Fact]
    public async Task An_address_already_in_the_book_opens_its_saved_connection()
    {
        var saved = new ConnectionProfile
        {
            Name = "Base",
            Host = "DB.example.org",
            Port = 2222,
            Username = "postgres",
            Authentication = AuthenticationKind.PrivateKey,
            PrivateKeyPath = @"C:\cle",
        };
        _repository.Save(saved);
        var vm = CreateViewModel();
        vm.QuickConnectText = "ssh -p 2222 postgres@db.example.org";

        await vm.QuickConnectCommand.ExecuteAsync(null);

        var opened = Assert.Single(_launcher.Terminals);
        Assert.False(opened.IsTransient);
        Assert.Equal(saved.Id, opened.Id);
    }

    [Fact]
    public async Task Saving_a_quick_connection_keeps_its_identity_and_fills_the_form()
    {
        Assert.True(QuickConnectTarget.TryParse("alice@serveur:2222", out var target));
        var transient = target.ToProfile();
        ConnectionEditorViewModel? form = null;
        _dialogs.OnEdit = editor =>
        {
            form = editor;
            editor.Folder = "Labo";
            return true;
        };
        var vm = CreateViewModel();

        var saved = await vm.SaveQuickConnectionAsync(transient);

        Assert.NotNull(form);
        Assert.True(form.IsNew);
        Assert.Equal(("serveur", 2222d, "alice"), (form.Host, form.Port, form.Username));
        Assert.NotNull(saved);
        Assert.Equal(transient.Id, saved.Id);
        Assert.False(saved.IsTransient);
        Assert.Equal("Labo", saved.Folder);
        Assert.Equal("alice@serveur:2222", saved.Name);
        Assert.Single(_repository.GetAll());
    }

    [Fact]
    public async Task Cancelling_the_form_saves_nothing()
    {
        Assert.True(QuickConnectTarget.TryParse("alice@serveur", out var target));
        _dialogs.OnEdit = _ => false;

        var saved = await CreateViewModel().SaveQuickConnectionAsync(target.ToProfile());

        Assert.Null(saved);
        Assert.Empty(_repository.GetAll());
    }

    [Fact]
    public async Task A_terminal_tab_offers_to_save_a_connected_quick_connection_then_adopts_the_saved_one()
    {
        Assert.True(QuickConnectTarget.TryParse("alice@serveur", out var target));
        var factory = new FakeSessionFactory();
        factory.Terminals.Enqueue(() => new FakeTerminalSession());
        var tab = new TerminalTabViewModel(target.ToProfile(), factory, new KeyStrings(), new ImmediateDispatcher());
        Assert.False(tab.ShowSaveOffer);

        await tab.ConnectAsync(80, 24);
        Assert.True(tab.ShowSaveOffer);

        var saved = tab.Profile.Clone();
        saved.IsTransient = false;
        saved.Name = "Labo";
        var changed = new List<string?>();
        tab.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        tab.AdoptSavedProfile(saved);

        Assert.False(tab.IsQuickConnect);
        Assert.False(tab.ShowSaveOffer);
        Assert.Equal("Labo", tab.Title);
        Assert.Contains(nameof(TerminalTabViewModel.Title), changed);
    }

    [Fact]
    public async Task Dismissing_the_offer_hides_it()
    {
        Assert.True(QuickConnectTarget.TryParse("alice@serveur", out var target));
        var factory = new FakeSessionFactory();
        factory.Terminals.Enqueue(() => new FakeTerminalSession());
        var tab = new TerminalTabViewModel(target.ToProfile(), factory, new KeyStrings(), new ImmediateDispatcher());
        await tab.ConnectAsync(80, 24);

        tab.SaveOfferDismissed = true;

        Assert.False(tab.ShowSaveOffer);
    }

    private sealed class RecordingLauncher : ISessionLauncher
    {
        public List<ConnectionProfile> Terminals { get; } = [];

        public void OpenTerminal(ConnectionProfile profile) => Terminals.Add(profile);

        public void OpenLocalTerminal(string? shellId = null)
        {
        }

        public void OpenSftp(ConnectionProfile profile)
        {
        }

        public Task OpenSftpHereAsync(TerminalTabViewModel tab) => Task.CompletedTask;
    }
}
