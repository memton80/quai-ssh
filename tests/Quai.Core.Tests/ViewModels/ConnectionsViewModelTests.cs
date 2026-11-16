using Quai.Core.Models;
using Quai.Core.Security;
using Quai.Core.Services;
using Quai.Core.Storage;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.ViewModels;

public sealed class ConnectionsViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly JsonConnectionRepository _repository;
    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeDialogService _dialogs = new();

    public ConnectionsViewModelTests()
    {
        _repository = new JsonConnectionRepository(_temp.File("connections.json"));
    }

    public void Dispose() => _temp.Dispose();

    private ConnectionsViewModel CreateViewModel() => new(_repository, _secrets, _dialogs, new KeyStrings());

    private static ConnectionFilterItem Folder(ConnectionsViewModel vm, string path) =>
        vm.Filters.Single(f => f.Kind == ConnectionFilterKind.Folder && f.Value == path);

    private ConnectionProfile Add(string name, string folder = "", params string[] tags)
    {
        var profile = new ConnectionProfile
        {
            Name = name,
            Host = name.ToLowerInvariant().Replace(' ', '-') + ".example.org",
            Username = "user",
            Folder = folder,
            Tags = [.. tags],
        };
        _repository.Save(profile);
        return profile;
    }

    [Fact]
    public void An_empty_book_is_flagged_as_empty()
    {
        var vm = CreateViewModel();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasNoResults);
        Assert.Empty(vm.Connections);
        Assert.Equal(ConnectionFilterKind.All, Assert.Single(vm.Filters).Kind);
    }

    [Fact]
    public void Filters_list_all_then_folders_with_ancestors_then_tags()
    {
        Add("A", "Prod/Web", "web");
        Add("B", "Prod", "db", "web");
        Add("C");

        var vm = CreateViewModel();

        Assert.Equal(
            ["All::3", "Folder:Prod:2", "Folder:Prod/Web:1", "Tag:db:1", "Tag:web:2"],
            vm.Filters.Select(f => $"{f.Kind}:{f.Value}:{f.Count}"));
        Assert.Equal("Web", vm.Filters[2].Label);
        Assert.Equal(1, vm.Filters[2].Depth);
        Assert.Equal(3, vm.Connections.Count);
    }

    [Fact]
    public void Only_folders_with_subfolders_can_be_collapsed()
    {
        Add("Proxmox", "HomeLab");
        Add("NAS", "HomeLab/data");
        Add("Web", "Prod");

        var vm = CreateViewModel();

        Assert.Equal(
            ["HomeLab:True:True", "HomeLab/data:False:True", "Prod:False:True"],
            vm.Filters.Where(f => f.Kind == ConnectionFilterKind.Folder).Select(f => $"{f.Value}:{f.HasChildren}:{f.IsExpanded}"));
        Assert.Equal("\uE70D", Folder(vm, "HomeLab").ChevronGlyph);
    }

    [Fact]
    public void Collapsing_a_folder_hides_its_subfolders_and_keeps_its_count()
    {
        Add("Proxmox", "HomeLab");
        Add("NAS", "HomeLab/data");
        Add("Sauvegardes", "HomeLab/data/backup");
        Add("Web", "Prod", "web");
        var vm = CreateViewModel();
        var homeLab = Folder(vm, "HomeLab");
        var changes = new List<string>();
        homeLab.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        vm.ToggleFolderCommand.Execute(homeLab);

        Assert.Equal(["All:", "Folder:HomeLab", "Folder:Prod", "Tag:web"], vm.Filters.Select(f => $"{f.Kind}:{f.Value}"));
        Assert.False(homeLab.IsExpanded);
        Assert.Equal(3, homeLab.Count);
        Assert.Equal("\uE76C", homeLab.ChevronGlyph);
        Assert.Contains(nameof(ConnectionFilterItem.ChevronGlyph), changes);

        vm.ToggleFolderCommand.Execute(homeLab);

        Assert.Equal(
            ["All:", "Folder:HomeLab", "Folder:HomeLab/data", "Folder:HomeLab/data/backup", "Folder:Prod", "Tag:web"],
            vm.Filters.Select(f => $"{f.Kind}:{f.Value}"));
        Assert.Same(homeLab, vm.Filters[1]);
    }

    [Fact]
    public void A_collapsed_subfolder_stays_collapsed_when_its_parent_is_reopened()
    {
        Add("NAS", "HomeLab/data");
        Add("Sauvegardes", "HomeLab/data/backup");
        var vm = CreateViewModel();

        vm.ToggleFolder(Folder(vm, "HomeLab/data"));
        vm.ToggleFolder(Folder(vm, "HomeLab"));
        vm.ToggleFolder(Folder(vm, "HomeLab"));

        Assert.Equal(["HomeLab", "HomeLab/data"], vm.Filters.Where(f => f.Kind == ConnectionFilterKind.Folder).Select(f => f.Value));
        Assert.False(Folder(vm, "HomeLab/data").IsExpanded);
    }

    [Fact]
    public void Collapsing_the_parent_of_the_selected_folder_selects_the_parent()
    {
        Add("Proxmox", "HomeLab");
        Add("NAS", "HomeLab/data");
        Add("Web", "Prod");
        var vm = CreateViewModel();
        vm.SelectedFilter = Folder(vm, "HomeLab/data");
        Assert.Equal(["NAS"], vm.Connections.Select(c => c.Name));

        vm.ToggleFolder(Folder(vm, "HomeLab"));

        Assert.Same(Folder(vm, "HomeLab"), vm.SelectedFilter);
        Assert.Equal(["NAS", "Proxmox"], vm.Connections.Select(c => c.Name).Order());
    }

    [Fact]
    public void Leaves_and_tags_do_not_collapse()
    {
        Add("NAS", "HomeLab/data", "nas");
        var vm = CreateViewModel();
        var before = vm.Filters.Select(f => f.Value).ToList();

        vm.ToggleFolder(Folder(vm, "HomeLab/data"));
        vm.ToggleFolder(vm.Filters.Single(f => f.Kind == ConnectionFilterKind.Tag));
        vm.ToggleFolder(vm.Filters[0]);
        vm.ToggleFolder(null);

        Assert.Equal(before, vm.Filters.Select(f => f.Value));
        Assert.All(vm.Filters, f => Assert.True(f.IsExpanded));
    }

    [Fact]
    public void Left_and_right_arrows_fold_like_the_explorer()
    {
        Add("Proxmox", "HomeLab");
        Add("NAS", "HomeLab/data");
        var vm = CreateViewModel();

        vm.SelectedFilter = Folder(vm, "HomeLab/data");
        Assert.True(vm.CollapseSelected());          // sous-dossier sans enfant : remonte au parent
        Assert.Equal("HomeLab", vm.SelectedFilter!.Value);
        Assert.True(vm.CollapseSelected());          // parent déplié : se replie
        Assert.False(Folder(vm, "HomeLab").IsExpanded);
        Assert.False(vm.CollapseSelected());         // déjà replié, à la racine : rien à faire
        Assert.True(vm.ExpandSelected());
        Assert.True(Folder(vm, "HomeLab").IsExpanded);
        Assert.False(vm.ExpandSelected());           // déjà déplié

        vm.SelectedFilter = vm.Filters[0];
        Assert.False(vm.CollapseSelected());         // « toutes » : la touche n'est pas utilisée
        Assert.False(vm.ExpandSelected());
    }

    [Fact]
    public async Task Folding_survives_a_reload_after_an_edit()
    {
        Add("Proxmox", "HomeLab");
        Add("NAS", "HomeLab/data");
        var vm = CreateViewModel();
        vm.ToggleFolder(Folder(vm, "HomeLab"));
        _dialogs.OnEdit = editor =>
        {
            editor.Name = "Proxmox VE";
            return true;
        };

        await vm.Connections.Single(c => c.Name == "Proxmox").EditCommand.ExecuteAsync(null);

        Assert.False(Folder(vm, "HomeLab").IsExpanded);
        Assert.DoesNotContain(vm.Filters, f => f.Value == "HomeLab/data");
    }

    [Fact]
    public void Selecting_a_folder_or_a_tag_narrows_the_list()
    {
        Add("A", "Prod/Web", "web");
        Add("B", "Prod", "db");
        Add("C");
        var vm = CreateViewModel();

        vm.SelectedFilter = vm.Filters.Single(f => f is { Kind: ConnectionFilterKind.Folder, Value: "Prod" });
        Assert.Equal(["A", "B"], vm.Connections.Select(c => c.Name));

        vm.SelectedFilter = vm.Filters.Single(f => f is { Kind: ConnectionFilterKind.Tag, Value: "db" });
        Assert.Equal(["B"], vm.Connections.Select(c => c.Name));
    }

    [Fact]
    public void Search_narrows_the_list_and_reports_no_results()
    {
        Add("Frontal");
        Add("Base");
        var vm = CreateViewModel();

        vm.SearchText = "front";
        Assert.Equal(["Frontal"], vm.Connections.Select(c => c.Name));
        Assert.False(vm.HasNoResults);

        vm.SearchText = "introuvable";
        Assert.Empty(vm.Connections);
        Assert.True(vm.HasNoResults);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task New_saves_the_connection_and_stores_the_password_in_the_vault_only()
    {
        var vm = CreateViewModel();
        _dialogs.OnEdit = editor =>
        {
            editor.Name = "Nouveau";
            editor.Host = "new.example.org";
            editor.Username = "alice";
            editor.Password = "s3cr3t-très-privé";
            return true;
        };

        await vm.NewCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.Connections);
        Assert.Equal("Nouveau", item.Name);
        Assert.Equal("s3cr3t-très-privé", _secrets.Items[SecretKeys.Password(item.Id)]);
        Assert.DoesNotContain("s3cr3t", File.ReadAllText(_temp.File("connections.json")));
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task New_does_nothing_when_the_dialog_is_cancelled()
    {
        var vm = CreateViewModel();
        _dialogs.OnEdit = _ => false;

        await vm.NewCommand.ExecuteAsync(null);

        Assert.Empty(_repository.GetAll());
    }

    [Fact]
    public async Task New_preselects_the_current_folder()
    {
        Add("A", "Prod");
        var vm = CreateViewModel();
        vm.SelectedFilter = vm.Filters.Single(f => f.Kind == ConnectionFilterKind.Folder);
        string? proposedFolder = null;
        _dialogs.OnEdit = editor =>
        {
            proposedFolder = editor.Folder;
            return false;
        };

        await vm.NewCommand.ExecuteAsync(null);

        Assert.Equal("Prod", proposedFolder);
    }

    [Fact]
    public async Task Editing_with_an_empty_password_keeps_the_stored_one()
    {
        var profile = Add("A");
        _secrets.Set(SecretKeys.Password(profile.Id), "ancien");
        var vm = CreateViewModel();
        _dialogs.OnEdit = editor =>
        {
            Assert.True(editor.HasStoredPassword);
            editor.Name = "A renommé";
            return true;
        };

        await vm.Connections[0].EditCommand.ExecuteAsync(null);

        Assert.Equal("A renommé", vm.Connections[0].Name);
        Assert.Equal("ancien", _secrets.Items[SecretKeys.Password(profile.Id)]);
    }

    [Fact]
    public async Task Unchecking_save_password_removes_it_from_the_vault()
    {
        var profile = Add("A");
        _secrets.Set(SecretKeys.Password(profile.Id), "ancien");
        var vm = CreateViewModel();
        _dialogs.OnEdit = editor =>
        {
            editor.SavePassword = false;
            return true;
        };

        await vm.Connections[0].EditCommand.ExecuteAsync(null);

        Assert.Empty(_secrets.Items);
    }

    [Fact]
    public async Task Switching_to_key_authentication_drops_the_password_and_stores_the_passphrase()
    {
        var profile = Add("A");
        _secrets.Set(SecretKeys.Password(profile.Id), "ancien");
        var vm = CreateViewModel();
        _dialogs.OnEdit = editor =>
        {
            editor.AuthenticationIndex = (int)AuthenticationKind.PrivateKey;
            editor.PrivateKeyPath = "C:\\keys\\id_ed25519";
            editor.Passphrase = "phrase";
            return true;
        };

        await vm.Connections[0].EditCommand.ExecuteAsync(null);

        Assert.False(_secrets.Items.ContainsKey(SecretKeys.Password(profile.Id)));
        Assert.Equal("phrase", _secrets.Items[SecretKeys.Passphrase(profile.Id)]);
        Assert.True(vm.Connections[0].UsesPrivateKey);
    }

    [Fact]
    public async Task A_vault_failure_is_reported_but_the_connection_is_still_saved()
    {
        var vm = CreateViewModel();
        _secrets.Fail = true;
        _dialogs.OnEdit = editor =>
        {
            editor.Host = "h.example.org";
            editor.Username = "u";
            editor.Password = "secret";
            return true;
        };

        await vm.NewCommand.ExecuteAsync(null);

        Assert.Single(_repository.GetAll());
        var error = Assert.Single(_dialogs.Errors);
        Assert.DoesNotContain("secret", error);
    }

    [Fact]
    public async Task Delete_asks_for_confirmation_then_removes_connection_and_secrets()
    {
        var profile = Add("A");
        _secrets.Set(SecretKeys.Password(profile.Id), "p");
        _secrets.Set(SecretKeys.Passphrase(profile.Id), "q");
        var vm = CreateViewModel();

        await vm.Connections[0].DeleteCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Confirmations);
        Assert.Empty(_repository.GetAll());
        Assert.Empty(_secrets.Items);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task Delete_is_cancelled_when_not_confirmed()
    {
        Add("A");
        var vm = CreateViewModel();
        _dialogs.ConfirmResult = false;

        await vm.Connections[0].DeleteCommand.ExecuteAsync(null);

        Assert.Single(_repository.GetAll());
    }

    [Fact]
    public void Duplicate_creates_an_independent_copy_with_its_own_secrets()
    {
        var profile = Add("A", "Prod", "web");
        _secrets.Set(SecretKeys.Password(profile.Id), "p");
        var vm = CreateViewModel();

        vm.Connections[0].DuplicateCommand.Execute(null);

        Assert.Equal(2, vm.Connections.Count);
        var copy = vm.Connections.Single(c => c.Id != profile.Id);
        Assert.Equal("Connections_CopyName(A)", copy.Name);
        Assert.Equal("Prod", copy.Folder);
        Assert.Equal("p", _secrets.Items[SecretKeys.Password(copy.Id)]);
    }

    [Fact]
    public async Task The_selected_filter_survives_a_reload()
    {
        Add("A", "Prod");
        Add("B", "Tests");
        var vm = CreateViewModel();
        vm.SelectedFilter = vm.Filters.Single(f => f.Value == "Tests");
        _dialogs.OnEdit = editor =>
        {
            editor.Notes = "modifiée";
            return true;
        };

        await vm.Connections[0].EditCommand.ExecuteAsync(null);

        Assert.Equal("Tests", vm.SelectedFilter?.Value);
        Assert.Equal(["B"], vm.Connections.Select(c => c.Name));
    }

    [Fact]
    public async Task A_serial_connection_is_saved_without_any_secret_and_drops_old_ones()
    {
        var existing = Add("Ancien serveur");
        _secrets.Items[SecretKeys.Password(existing.Id)] = "secret";
        _secrets.Items[SecretKeys.Passphrase(existing.Id)] = "phrase";
        _dialogs.OnEdit = editor =>
        {
            editor.ProtocolIndex = (int)ConnectionProtocol.Serial;
            editor.SerialPortName = "COM3";
            editor.Password = "ne-doit-pas-sortir";
            return true;
        };
        var vm = CreateViewModel();

        await vm.EditAsync(vm.Connections.Single());

        var saved = _repository.Find(existing.Id)!;
        Assert.True(saved.IsSerial);
        Assert.Equal("COM3", saved.Serial!.PortName);
        Assert.Empty(_secrets.Items);
        var item = Assert.Single(vm.Connections);
        Assert.True(item.IsSerial);
        Assert.False(item.SupportsSftp);
        Assert.False(item.UsesPrivateKey);
    }

    [Fact]
    public async Task The_new_connection_form_lists_the_serial_ports_of_this_computer()
    {
        var ports = new FakeSerialPortCatalog { Ports = { "COM4" } };
        ConnectionEditorViewModel? shown = null;
        _dialogs.OnEdit = editor =>
        {
            shown = editor;
            editor.ProtocolIndex = (int)ConnectionProtocol.Serial;
            return false;
        };
        var vm = new ConnectionsViewModel(_repository, _secrets, _dialogs, new KeyStrings(), serialPorts: ports);

        await vm.NewAsync();

        Assert.Equal(["COM4"], shown!.SerialPorts);
        Assert.Equal("COM4", shown.SerialPortName);
    }

    [Fact]
    public void Sftp_is_not_opened_on_a_serial_connection_but_the_terminal_is()
    {
        var terminals = new TerminalsViewModel(new FakeSessionFactory(), new KeyStrings(), new ImmediateDispatcher());
        var sftp = new SftpSessionsViewModel(new FakeSessionFactory(), _dialogs, new KeyStrings(), new ImmediateDispatcher());
        var launcher = new SessionLauncher(terminals, sftp, new RecordingNavigator());
        _repository.Save(new ConnectionProfile
        {
            Name = "Console",
            Protocol = ConnectionProtocol.Serial,
            Serial = new SerialSettings { PortName = "COM3" },
        });
        var vm = new ConnectionsViewModel(_repository, _secrets, _dialogs, new KeyStrings(), launcher);
        var item = vm.Connections.Single();

        vm.OpenSftp(item);
        launcher.OpenSftp(item.Profile);
        vm.OpenTerminal(item);

        Assert.Empty(sftp.Tabs);
        Assert.True(Assert.Single(terminals.Tabs).IsSerial);
    }
}
