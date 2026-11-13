using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Sftp;
using Quai.Core.Ssh;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;
using Renci.SshNet.Common;

namespace Quai.Core.Tests.ViewModels;

public sealed class SftpBrowserViewModelTests : IDisposable
{
    private readonly TempDirectory _local = new();
    private readonly FakeSftpSession _remote = new();
    private readonly FakeSessionFactory _factory = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeRecycler _recycler = new();
    private readonly ConnectionProfile _profile = new() { Name = "Serveur", Host = "example.org", Username = "alice" };

    public SftpBrowserViewModelTests()
    {
        _remote.AddDirectory("/home/alice/docs");
        _remote.AddFile("/home/alice/notes.txt", "bonjour");
        _remote.AddFile("/home/alice/Zèbre.txt", "z");
        _remote.AddFile("/home/alice/docs/rapport.txt", "contenu du rapport");
    }

    public void Dispose() => _local.Dispose();

    private SftpBrowserViewModel Create() =>
        new(_profile, _factory, _dialogs, new KeyStrings(), new ImmediateDispatcher(), _recycler, initialLocalPath: _local.Path);

    private async Task<SftpBrowserViewModel> ConnectedAsync()
    {
        _factory.SftpSessions.Enqueue(() => _remote);
        var vm = Create();
        await vm.ConnectAsync();
        Assert.Equal(SftpStatus.Connected, vm.Status);
        return vm;
    }

    private string LocalFile(string name, string content = "local")
    {
        var path = _local.File(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string[] Names(IEnumerable<FileItemViewModel> items) => [.. items.Select(i => i.Name)];

    private static FileItemViewModel Item(IEnumerable<FileItemViewModel> items, string name) => items.Single(i => i.Name == name);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition non atteinte dans le délai.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    // ---- Connexion et navigation ---------------------------------------------------------------

    [Fact]
    public async Task Connecting_shows_the_home_directory_with_folders_first()
    {
        var vm = await ConnectedAsync();

        Assert.Equal("/home/alice", vm.RemotePath);
        Assert.Equal(["..", "docs", "notes.txt", "Zèbre.txt"], Names(vm.RemoteItems));
        Assert.True(vm.RemoteItems[0].IsParent);
        Assert.Equal("-rw-r--r--", Item(vm.RemoteItems, "notes.txt").PermissionsText);
        Assert.Equal("drwxr-xr-x", Item(vm.RemoteItems, "docs").PermissionsText);
        Assert.Equal(string.Empty, Item(vm.RemoteItems, "docs").SizeText);
        Assert.False(vm.IsRemoteLoading);
    }

    [Fact]
    public async Task A_failed_connection_is_explained_and_can_be_retried()
    {
        _factory.SftpSessions.Enqueue(() => throw new SshLoginFailedException("refus"));
        _factory.SftpSessions.Enqueue(() => _remote);
        var vm = Create();

        await vm.ConnectAsync();
        Assert.Equal(SftpStatus.Failed, vm.Status);
        Assert.Equal("Terminal_AuthenticationFailed", vm.StatusText);
        Assert.True(vm.CanReconnect);

        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.True(vm.IsConnected);
        Assert.Equal(string.Empty, vm.StatusText);
    }

    [Fact]
    public async Task Remote_navigation_enters_folders_goes_up_and_accepts_typed_paths()
    {
        var vm = await ConnectedAsync();

        await vm.OpenRemoteAsync(Item(vm.RemoteItems, "docs"));
        Assert.Equal("/home/alice/docs", vm.RemotePath);
        Assert.Equal(["..", "rapport.txt"], Names(vm.RemoteItems));

        await vm.RemoteUpCommand.ExecuteAsync(null);
        Assert.Equal("/home/alice", vm.RemotePath);

        await vm.NavigateRemoteAsync("docs/../docs/");
        Assert.Equal("/home/alice/docs", vm.RemotePath);

        await vm.NavigateRemoteAsync("/");
        Assert.Equal("/", vm.RemotePath);
        Assert.DoesNotContain(vm.RemoteItems, i => i.IsParent);
    }

    [Fact]
    public async Task An_invalid_remote_path_reports_an_error_and_keeps_the_current_folder()
    {
        var vm = await ConnectedAsync();

        await vm.NavigateRemoteAsync("/nulle/part");

        Assert.Equal("/home/alice", vm.RemotePath);
        Assert.Equal("Sftp_RemoteError(Sftp_PathNotFound)", Assert.Single(_dialogs.Errors));
        Assert.Equal(4, vm.RemoteItems.Count);
    }

    [Fact]
    public void The_local_panel_lists_navigates_and_reports_bad_paths()
    {
        LocalFile("b.txt");
        LocalFile(Path.Combine("dossier", "a.txt"));
        var vm = Create();
        vm.RefreshLocalCommand.Execute(null);

        Assert.Equal(["..", "dossier", "b.txt"], Names(vm.LocalItems));

        vm.OpenLocal(Item(vm.LocalItems, "dossier"));
        Assert.Equal(_local.File("dossier"), vm.LocalPath);
        Assert.Equal(["..", "a.txt"], Names(vm.LocalItems));

        vm.LocalUpCommand.Execute(null);
        Assert.Equal(_local.Path, vm.LocalPath);

        vm.NavigateLocal(_local.File("absent"));
        Assert.Equal(_local.Path, vm.LocalPath);
        Assert.Single(_dialogs.Errors);
    }

    // ---- Transferts --------------------------------------------------------------------------

    [Fact]
    public async Task Uploading_a_file_runs_the_transfer_and_refreshes_the_remote_panel()
    {
        var vm = await ConnectedAsync();
        var source = LocalFile("envoi.txt", "à envoyer");

        await vm.UploadAsync([source]);
        var transfer = Assert.Single(vm.Transfers);
        await WaitForAsync(() => !transfer.IsActive);

        Assert.Equal(TransferStatus.Completed, transfer.Status);
        Assert.Equal(100, transfer.Percent);
        Assert.Equal(transfer.TotalBytes, transfer.TransferredBytes);
        Assert.StartsWith("Transfer_Completed", transfer.Detail);
        Assert.Equal("à envoyer", _remote.ReadText("/home/alice/envoi.txt"));
        Assert.Contains("envoi.txt", Names(vm.RemoteItems));
        Assert.True(vm.HasTransfers);
    }

    [Fact]
    public async Task Uploading_a_folder_sends_its_whole_tree()
    {
        var vm = await ConnectedAsync();
        LocalFile(Path.Combine("site", "index.html"), "<html>");
        LocalFile(Path.Combine("site", "css", "style.css"), "body{}");
        vm.RefreshLocalCommand.Execute(null);

        await vm.UploadAsync([Item(vm.LocalItems, "site"), vm.LocalItems[0]]);
        await WaitForAsync(() => vm.Transfers.All(t => !t.IsActive));

        Assert.Single(vm.Transfers);
        Assert.Equal("<html>", _remote.ReadText("/home/alice/site/index.html"));
        Assert.Equal("body{}", _remote.ReadText("/home/alice/site/css/style.css"));
        Assert.Equal(12, vm.Transfers[0].TotalBytes);
    }

    [Fact]
    public async Task Overwriting_remote_files_requires_confirmation()
    {
        var vm = await ConnectedAsync();
        var source = LocalFile("notes.txt", "nouvelle version");

        _dialogs.ConfirmResult = false;
        await vm.UploadAsync([source]);
        Assert.Empty(vm.Transfers);
        Assert.Equal("Sftp_OverwriteOne(notes.txt)", Assert.Single(_dialogs.Confirmations));
        Assert.Equal("bonjour", _remote.ReadText("/home/alice/notes.txt"));

        _dialogs.ConfirmResult = true;
        await vm.UploadAsync([source]);
        await WaitForAsync(() => vm.Transfers.All(t => !t.IsActive));
        Assert.Equal("nouvelle version", _remote.ReadText("/home/alice/notes.txt"));
    }

    [Fact]
    public async Task Downloading_writes_into_the_local_folder_and_refreshes_it()
    {
        var vm = await ConnectedAsync();

        await vm.DownloadAsync([Item(vm.RemoteItems, "notes.txt"), Item(vm.RemoteItems, "docs"), vm.RemoteItems[0]]);
        await WaitForAsync(() => vm.Transfers.All(t => !t.IsActive));

        Assert.Equal(2, vm.Transfers.Count);
        Assert.All(vm.Transfers, t => Assert.Equal(TransferStatus.Completed, t.Status));
        Assert.Equal("bonjour", File.ReadAllText(_local.File("notes.txt")));
        Assert.Equal("contenu du rapport", File.ReadAllText(Path.Combine(_local.Path, "docs", "rapport.txt")));
        Assert.Equal(["..", "docs", "notes.txt"], Names(vm.LocalItems));
    }

    [Fact]
    public async Task Overwriting_local_files_requires_confirmation()
    {
        LocalFile("notes.txt", "version locale");
        var vm = await ConnectedAsync();
        vm.RefreshLocalCommand.Execute(null);
        _dialogs.ConfirmResult = false;

        await vm.DownloadAsync([Item(vm.RemoteItems, "notes.txt")]);

        Assert.Empty(vm.Transfers);
        Assert.Equal("version locale", File.ReadAllText(_local.File("notes.txt")));
    }

    [Fact]
    public async Task A_running_transfer_can_be_cancelled_and_the_queue_moves_on()
    {
        var vm = await ConnectedAsync();
        _remote.TransferGate = new TaskCompletionSource();
        var first = LocalFile("un.bin", new string('1', 1000));
        var second = LocalFile("deux.bin", new string('2', 10));

        await vm.UploadAsync([first, second]);
        var running = vm.Transfers[0];
        var waiting = vm.Transfers[1];
        await WaitForAsync(() => running.TransferredBytes > 0);

        Assert.Equal(TransferStatus.Running, running.Status);
        Assert.Equal(TransferStatus.Pending, waiting.Status);
        Assert.Equal(50, running.Percent);
        Assert.True(running.CancelCommand.CanExecute(null));

        running.CancelCommand.Execute(null);
        await WaitForAsync(() => !running.IsActive);
        _remote.TransferGate.SetResult();
        await WaitForAsync(() => !waiting.IsActive);

        Assert.Equal(TransferStatus.Cancelled, running.Status);
        Assert.Equal("Transfer_Cancelled", running.Detail);
        Assert.False(running.CancelCommand.CanExecute(null));
        Assert.False(_remote.Contains("/home/alice/un.bin"));
        Assert.Equal(TransferStatus.Completed, waiting.Status);
        Assert.True(_remote.Contains("/home/alice/deux.bin"));
    }

    [Fact]
    public async Task A_pending_transfer_can_be_cancelled_before_it_starts()
    {
        var vm = await ConnectedAsync();
        _remote.TransferGate = new TaskCompletionSource();
        await vm.UploadAsync([LocalFile("un.bin"), LocalFile("deux.bin")]);
        var waiting = vm.Transfers[1];

        waiting.Cancel();
        _remote.TransferGate.SetResult();
        await WaitForAsync(() => vm.Transfers.All(t => !t.IsActive));

        Assert.Equal(TransferStatus.Cancelled, waiting.Status);
        Assert.False(_remote.Contains("/home/alice/deux.bin"));
        Assert.True(_remote.Contains("/home/alice/un.bin"));
    }

    [Fact]
    public async Task A_failed_transfer_shows_the_reason_and_finished_ones_can_be_cleared()
    {
        var vm = await ConnectedAsync();
        _remote.NextError = new SftpPermissionDeniedException("Permission denied");

        await vm.UploadAsync([LocalFile("refuse.txt")]);
        await WaitForAsync(() => vm.Transfers.All(t => !t.IsActive));

        Assert.Equal(TransferStatus.Failed, vm.Transfers[0].Status);
        Assert.Equal("Transfer_Failed(Sftp_PermissionDenied)", vm.Transfers[0].Detail);

        vm.ClearFinishedTransfersCommand.Execute(null);
        Assert.Empty(vm.Transfers);
        Assert.False(vm.HasTransfers);
    }

    // ---- Opérations distantes ----------------------------------------------------------------

    [Fact]
    public async Task A_new_remote_folder_is_created_from_the_prompt()
    {
        var vm = await ConnectedAsync();
        _dialogs.TextAnswers.Enqueue(" archives ");

        await vm.NewRemoteFolderCommand.ExecuteAsync(null);

        Assert.Contains("mkdir /home/alice/archives", _remote.Operations);
        Assert.Contains("archives", Names(vm.RemoteItems));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("..")]
    public async Task Invalid_names_are_refused(string name)
    {
        var vm = await ConnectedAsync();
        _dialogs.TextAnswers.Enqueue(name);

        await vm.NewRemoteFolderCommand.ExecuteAsync(null);

        Assert.Empty(_remote.Operations);
        Assert.Equal($"Sftp_InvalidName({name})", Assert.Single(_dialogs.Errors));
    }

    [Fact]
    public async Task Cancelling_the_prompt_does_nothing()
    {
        var vm = await ConnectedAsync();
        _dialogs.TextAnswers.Enqueue(null);

        await vm.NewRemoteFolderCommand.ExecuteAsync(null);
        await vm.RenameRemoteAsync(Item(vm.RemoteItems, "notes.txt"));

        Assert.Empty(_remote.Operations);
    }

    [Fact]
    public async Task Renaming_proposes_the_current_name_and_renames()
    {
        var vm = await ConnectedAsync();
        _dialogs.TextAnswers.Enqueue("journal.txt");

        await vm.RenameRemoteAsync(Item(vm.RemoteItems, "notes.txt"));

        Assert.Equal("notes.txt", _dialogs.TextRequests[0].Initial);
        Assert.Contains("rename /home/alice/notes.txt /home/alice/journal.txt", _remote.Operations);
        Assert.Contains("journal.txt", Names(vm.RemoteItems));
        Assert.DoesNotContain("notes.txt", Names(vm.RemoteItems));
    }

    [Fact]
    public async Task Renaming_or_deleting_a_link_never_touches_its_target()
    {
        _remote.AddLink("/home/alice/raccourci", toDirectory: true);
        var vm = await ConnectedAsync();
        _dialogs.TextAnswers.Enqueue("lien");

        await vm.RenameRemoteAsync(Item(vm.RemoteItems, "raccourci"));
        await vm.DeleteRemoteAsync([Item(vm.RemoteItems, "lien")]);

        Assert.Equal(
            ["rename-link /home/alice/raccourci /home/alice/lien", "delete-link /home/alice/lien"],
            _remote.Operations);
    }

    [Fact]
    public async Task Remote_deletion_always_asks_first()
    {
        var vm = await ConnectedAsync();
        _dialogs.ConfirmResult = false;

        await vm.DeleteRemoteAsync([Item(vm.RemoteItems, "docs")]);

        Assert.Equal("Sftp_DeleteRemoteOne(docs)", Assert.Single(_dialogs.Confirmations));
        Assert.Empty(_remote.Operations);
        Assert.True(_remote.Contains("/home/alice/docs/rapport.txt"));
    }

    [Fact]
    public async Task Confirmed_remote_deletion_removes_folders_recursively_but_never_the_parent_row()
    {
        var vm = await ConnectedAsync();

        await vm.DeleteRemoteAsync([vm.RemoteItems[0], Item(vm.RemoteItems, "docs"), Item(vm.RemoteItems, "notes.txt")]);

        Assert.Equal("Sftp_DeleteRemoteMany(2,docs, notes.txt)", Assert.Single(_dialogs.Confirmations));
        Assert.Equal(
            ["delete /home/alice/docs/rapport.txt", "rmdir /home/alice/docs", "delete /home/alice/notes.txt"],
            _remote.Operations);
        Assert.Equal(["..", "Zèbre.txt"], Names(vm.RemoteItems));
        Assert.True(_remote.Contains("/home"));
    }

    [Fact]
    public async Task A_refused_operation_is_reported_and_the_panel_stays_usable()
    {
        var vm = await ConnectedAsync();
        _remote.NextError = new SftpPermissionDeniedException("Permission denied");

        await vm.DeleteRemoteAsync([Item(vm.RemoteItems, "notes.txt")]);

        Assert.Equal("Sftp_RemoteError(Sftp_PermissionDenied)", Assert.Single(_dialogs.Errors));
        Assert.Contains("notes.txt", Names(vm.RemoteItems));
        Assert.False(vm.IsRemoteLoading);
    }

    [Fact]
    public async Task Permissions_are_changed_only_when_the_editor_is_confirmed_with_a_new_mode()
    {
        var vm = await ConnectedAsync();
        var item = Item(vm.RemoteItems, "notes.txt");

        _dialogs.OnEditPermissions = _ => false;
        await vm.EditPermissionsAsync(item);
        _dialogs.OnEditPermissions = _ => true;
        await vm.EditPermissionsAsync(item);
        Assert.Empty(_remote.Operations);

        _dialogs.OnEditPermissions = editor =>
        {
            Assert.Equal("644", editor.Octal);
            editor.OwnerExecute = true;
            editor.OthersRead = false;
            return true;
        };
        await vm.EditPermissionsAsync(item);

        Assert.Equal(["chmod 740 /home/alice/notes.txt"], _remote.Operations);
        Assert.Equal("-rwxr-----", Item(vm.RemoteItems, "notes.txt").PermissionsText);
    }

    // ---- Opérations locales ------------------------------------------------------------------

    [Fact]
    public async Task Local_folders_are_created_renamed_and_recycled()
    {
        var vm = Create();
        _dialogs.TextAnswers.Enqueue("nouveau");
        await vm.NewLocalFolderCommand.ExecuteAsync(null);
        Assert.True(Directory.Exists(_local.File("nouveau")));

        _dialogs.TextAnswers.Enqueue("renommé");
        await vm.RenameLocalAsync(Item(vm.LocalItems, "nouveau"));
        Assert.True(Directory.Exists(_local.File("renommé")));
        Assert.Equal(["..", "renommé"], Names(vm.LocalItems));

        _dialogs.ConfirmResult = false;
        await vm.DeleteLocalAsync([Item(vm.LocalItems, "renommé")]);
        Assert.Empty(_recycler.Recycled);

        _dialogs.ConfirmResult = true;
        await vm.DeleteLocalAsync([Item(vm.LocalItems, "renommé"), vm.LocalItems[0]]);
        Assert.Equal([_local.File("renommé")], _recycler.Recycled);
        Assert.Equal([".."], Names(vm.LocalItems));
    }

    // ---- Déconnexion -------------------------------------------------------------------------

    [Fact]
    public async Task A_lost_connection_disables_remote_operations_until_reconnection()
    {
        var vm = await ConnectedAsync();
        await vm.OpenRemoteAsync(Item(vm.RemoteItems, "docs"));

        _remote.Drop(new IOException("coupure"));

        Assert.Equal(SftpStatus.Disconnected, vm.Status);
        Assert.Equal("Terminal_ConnectionLost(coupure)", vm.StatusText);
        Assert.True(_remote.Disposed);

        await vm.UploadAsync([LocalFile("x.txt")]);
        await vm.DeleteRemoteAsync(vm.RemoteItems);
        await vm.RefreshRemoteCommand.ExecuteAsync(null);
        Assert.Empty(vm.Transfers);
        Assert.Empty(_dialogs.Confirmations);

        // À la reconnexion on retrouve le dossier où l'on était.
        var again = new FakeSftpSession();
        again.AddDirectory("/home/alice/docs");
        _factory.SftpSessions.Enqueue(() => again);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("/home/alice/docs", vm.RemotePath);
    }

    [Fact]
    public async Task Tabs_open_connect_and_close_and_the_launcher_shows_the_page()
    {
        _factory.SftpSessions.Enqueue(() => _remote);
        var sessions = new SftpSessionsViewModel(_factory, _dialogs, new KeyStrings(), new ImmediateDispatcher(), _recycler);
        var terminals = new TerminalsViewModel(_factory, new KeyStrings(), new ImmediateDispatcher());
        var navigator = new RecordingNavigator();
        Assert.True(sessions.IsEmpty);

        new SessionLauncher(terminals, sessions, navigator).OpenSftp(_profile);

        var tab = Assert.Single(sessions.Tabs);
        Assert.Same(tab, sessions.SelectedTab);
        Assert.Equal([AppPage.Sftp], navigator.Pages);
        await WaitForAsync(() => tab.IsConnected);

        await sessions.CloseAsync(tab);
        Assert.True(sessions.IsEmpty);
        Assert.True(_remote.Disposed);
    }
}

public class SftpSupportTests
{
    [Fact]
    public void The_permissions_editor_keeps_checkboxes_and_octal_in_sync()
    {
        var editor = new PermissionsEditorViewModel("script.sh", 0b110_100_100);
        Assert.Equal("644", editor.Octal);
        Assert.Equal("rw-r--r--", editor.Symbolic);
        Assert.True(editor.OwnerRead && editor.OwnerWrite && !editor.OwnerExecute);

        editor.OwnerExecute = true;
        editor.GroupWrite = true;
        editor.OthersRead = false;
        Assert.Equal("760", editor.Octal);

        editor.Octal = "755";
        Assert.Equal(0b111_101_101, editor.Mode);
        Assert.True(editor.OthersExecute);
        Assert.False(editor.GroupWrite);

        editor.Octal = "9x";
        Assert.False(editor.IsValid);
        Assert.Equal(0b111_101_101, editor.Mode);
    }

    [Fact]
    public void Property_changes_are_announced_for_the_checkboxes()
    {
        var editor = new PermissionsEditorViewModel("f", 0);
        var changed = new List<string?>();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        editor.Octal = "700";

        Assert.Contains(nameof(PermissionsEditorViewModel.OwnerRead), changed);
        Assert.Contains(nameof(PermissionsEditorViewModel.Symbolic), changed);
    }

    [Theory]
    [InlineData(0, "0 o")]
    [InlineData(1023, "1023 o")]
    [InlineData(1024, "1 Ko")]
    [InlineData(10 * 1024 * 1024, "10 Mo")]
    [InlineData(250L * 1024 * 1024 * 1024, "250 Go")]
    public void Sizes_use_binary_multiples_and_the_localised_units(long bytes, string expected) =>
        Assert.Equal(expected, new FileSizeFormatter(new UnitStrings()).Format(bytes));

    [Fact]
    public void Sizes_keep_one_decimal_below_100()
    {
        var text = new FileSizeFormatter(new UnitStrings()).Format(1536);

        Assert.Matches(@"^1[.,]5 Ko$", text);
    }

    [Fact]
    public void Local_listing_puts_folders_first_and_knows_parents()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("a.txt"), "12345");
        Directory.CreateDirectory(temp.File("zz"));

        var entries = LocalFileSystem.List(temp.Path);

        Assert.Equal(["zz", "a.txt"], entries.Select(e => e.Name));
        Assert.Equal(5, entries[1].Size);
        Assert.Equal(temp.Path, LocalFileSystem.GetParent(temp.File("zz")));
        Assert.Null(LocalFileSystem.GetParent(LocalFileSystem.Computer));
        Assert.NotEmpty(LocalFileSystem.List(LocalFileSystem.Computer));
    }

    [Theory]
    [InlineData("rapport.txt", true)]
    [InlineData("", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    public void Local_names_are_validated(string name, bool expected) =>
        Assert.Equal(expected, LocalFileSystem.IsValidName(name));

    [Fact]
    public void Transfer_progress_is_a_percentage()
    {
        var transfer = new TransferItemViewModel(TransferDirection.Download, "f", "/f", "C:\\");
        Assert.Equal(0, transfer.Percent);
        Assert.True(transfer.IsActive);

        transfer.TotalBytes = 200;
        transfer.TransferredBytes = 50;
        Assert.Equal(25, transfer.Percent);

        transfer.Status = TransferStatus.Completed;
        Assert.Equal(100, transfer.Percent);
        Assert.False(transfer.IsActive);
    }

    private sealed class UnitStrings : Quai.Core.Localization.IStrings
    {
        public string Get(string key) => key == "Size_Units" ? "o;Ko;Mo;Go;To" : key;

        public string Format(string key, params object?[] args) => key;
    }
}
