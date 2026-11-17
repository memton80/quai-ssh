using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Sftp;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.ViewModels;

public sealed class RemoteEditTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeSftpSession _remote = new();
    private readonly FakeSessionFactory _factory = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeLauncher _launcher = new();
    private readonly RemoteEditFiles _files;
    private readonly ConnectionProfile _profile = new() { Name = "Serveur", Host = "example.org", Username = "alice" };

    public RemoteEditTests()
    {
        _files = new RemoteEditFiles(_temp.File("edits"));
        _remote.AddFile("/home/alice/nginx.conf", "worker_processes 1;", mode: 0b110_100_000);
        _remote.AddFile("/home/alice/deploy.sh", "#!/bin/sh", mode: 0b111_101_101);
    }

    public void Dispose() => _temp.Dispose();

    private async Task<SftpBrowserViewModel> ConnectedAsync(FakeSftpSession? remote = null)
    {
        _factory.SftpSessions.Enqueue(() => remote ?? _remote);
        var vm = new SftpBrowserViewModel(
            _profile, _factory, _dialogs, new KeyStrings(), new ImmediateDispatcher(),
            initialLocalPath: _temp.Path, launcher: _launcher, editFiles: _files);
        await vm.ConnectAsync();
        Assert.True(vm.IsConnected);
        return vm;
    }

    private static FileItemViewModel Item(SftpBrowserViewModel vm, string name) => vm.RemoteItems.Single(i => i.Name == name);

    private async Task<(SftpBrowserViewModel Vm, RemoteEdit Edit)> EditingAsync(string name = "nginx.conf")
    {
        var vm = await ConnectedAsync();
        await vm.EditRemoteAsync(Item(vm, name));
        return (vm, Assert.Single(vm.Edits));
    }

    [Fact]
    public async Task Edit_downloads_a_private_copy_and_opens_it()
    {
        var (vm, edit) = await EditingAsync();

        var (path, choose) = Assert.Single(_launcher.Opened);
        Assert.Equal(edit.LocalPath, path);
        Assert.False(choose);
        Assert.Equal("nginx.conf", Path.GetFileName(path));
        Assert.StartsWith(_files.Root, path, StringComparison.Ordinal);
        Assert.Equal("worker_processes 1;", File.ReadAllText(path));
        Assert.True(vm.HasEdits);
        Assert.Equal("Sftp_EditsOne(nginx.conf)", vm.EditsText);
        Assert.False(edit.HasLocalChanges());
    }

    [Fact]
    public async Task A_saved_change_goes_back_in_place_and_keeps_the_permissions()
    {
        var (vm, edit) = await EditingAsync("deploy.sh");
        File.WriteAllText(edit.LocalPath, "#!/bin/sh\necho déployé");

        await vm.UploadEditAsync(edit);

        Assert.Equal("#!/bin/sh\necho déployé", _remote.ReadText("/home/alice/deploy.sh"));
        Assert.Equal(0b111_101_101, _remote.ModeOf("/home/alice/deploy.sh"));
        var transfer = Assert.Single(vm.Transfers);
        Assert.Equal(TransferStatus.Completed, transfer.Status);
        Assert.False(transfer.IsCancellable);
        Assert.False(edit.HasLocalChanges());
        Assert.Empty(_dialogs.Confirmations);
        Assert.DoesNotContain(Directory.GetFiles(Path.GetDirectoryName(edit.LocalPath)!), f => f.EndsWith(".quai-envoi", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_change_made_on_the_server_meanwhile_asks_before_overwriting()
    {
        var (vm, edit) = await EditingAsync();
        _remote.ChangeOnServer("/home/alice/nginx.conf", "worker_processes 8;");
        File.WriteAllText(edit.LocalPath, "worker_processes 2;");
        _dialogs.ConfirmResult = false;

        await vm.UploadEditAsync(edit);

        Assert.Equal("Sftp_EditConflictMessage(nginx.conf)", Assert.Single(_dialogs.Confirmations));
        Assert.Equal("worker_processes 8;", _remote.ReadText("/home/alice/nginx.conf"));
        Assert.Empty(vm.Transfers);

        _dialogs.ConfirmResult = true;
        await vm.UploadEditAsync(edit);

        Assert.Equal("worker_processes 2;", _remote.ReadText("/home/alice/nginx.conf"));

        // L'état du serveur est de nouveau connu : l'enregistrement suivant ne demande plus rien.
        File.WriteAllText(edit.LocalPath, "worker_processes 3;");
        await vm.UploadEditAsync(edit);
        Assert.Equal(2, _dialogs.Confirmations.Count);
        Assert.Equal("worker_processes 3;", _remote.ReadText("/home/alice/nginx.conf"));
    }

    [Fact]
    public async Task A_file_deleted_on_the_server_is_only_recreated_on_request()
    {
        var (vm, edit) = await EditingAsync();
        await _remote.DeleteFileAsync("/home/alice/nginx.conf", TestContext.Current.CancellationToken);
        File.WriteAllText(edit.LocalPath, "worker_processes 2;");

        await vm.UploadEditAsync(edit);

        Assert.Equal("Sftp_EditDeletedMessage(nginx.conf)", Assert.Single(_dialogs.Confirmations));
        Assert.Equal("worker_processes 2;", _remote.ReadText("/home/alice/nginx.conf"));
    }

    [Fact]
    public async Task Saves_made_while_disconnected_are_sent_after_reconnecting()
    {
        var (vm, edit) = await EditingAsync();
        _remote.Drop(new IOException("réseau"));
        File.WriteAllText(edit.LocalPath, "worker_processes 4;");

        await vm.UploadEditAsync(edit);

        Assert.True(edit.Pending);
        Assert.Equal("Sftp_EditsOnePending(nginx.conf)", vm.EditsText);

        var again = new FakeSftpSession();
        again.AddFile("/home/alice/nginx.conf", "worker_processes 1;");
        _factory.SftpSessions.Enqueue(() => again);
        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.False(edit.Pending);
        Assert.Equal("worker_processes 4;", again.ReadText("/home/alice/nginx.conf"));
        Assert.Equal("Sftp_EditsOne(nginx.conf)", vm.EditsText);
    }

    [Fact]
    public async Task A_failed_upload_keeps_the_change_for_the_next_save()
    {
        var (vm, edit) = await EditingAsync();
        File.WriteAllText(edit.LocalPath, "worker_processes 5;");
        _remote.NextError = new Renci.SshNet.Common.SftpPermissionDeniedException("Permission denied");

        await vm.UploadEditAsync(edit);

        Assert.True(edit.Pending);
        var transfer = Assert.Single(vm.Transfers);
        Assert.Equal(TransferStatus.Failed, transfer.Status);
        Assert.Equal("Sftp_EditFailed(Sftp_PermissionDenied)", transfer.Detail);
        Assert.True(edit.HasLocalChanges());

        await vm.UploadEditAsync(edit);
        Assert.Equal("worker_processes 5;", _remote.ReadText("/home/alice/nginx.conf"));
        Assert.False(edit.Pending);
    }

    [Fact]
    public async Task Opening_the_same_file_again_reuses_its_copy()
    {
        var (vm, edit) = await EditingAsync();
        File.WriteAllText(edit.LocalPath, "brouillon local");

        await vm.EditRemoteAsync(Item(vm, "nginx.conf"), chooseApplication: true);

        Assert.Same(edit, Assert.Single(vm.Edits));
        Assert.Equal((edit.LocalPath, true), _launcher.Opened[^1]);
        Assert.Equal("brouillon local", File.ReadAllText(edit.LocalPath));
        Assert.Single(_remote.Operations, o => o.StartsWith("download", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Closing_the_tab_stops_following_and_deletes_the_copies()
    {
        var (vm, edit) = await EditingAsync();
        var directory = Path.GetDirectoryName(edit.LocalPath)!;

        await vm.DisposeAsync();
        File.WriteAllText(_temp.File("ignoré"), string.Empty);

        Assert.False(Directory.Exists(directory));
        Assert.Empty(vm.Edits);
        await vm.UploadEditAsync(edit);
        Assert.DoesNotContain(_remote.Operations, o => o.StartsWith("upload", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Closing_the_tab_waits_for_an_upload_in_progress()
    {
        var (vm, edit) = await EditingAsync();
        File.WriteAllText(edit.LocalPath, "worker_processes 6;");
        _remote.TransferGate = new TaskCompletionSource();
        var upload = vm.UploadEditAsync(edit);

        var closing = vm.DisposeAsync().AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(closing.IsCompleted);
        Assert.False(_remote.Disposed);

        _remote.TransferGate.SetResult();
        await upload;
        await closing;

        Assert.Equal("worker_processes 6;", _remote.ReadText("/home/alice/nginx.conf"));
        Assert.Contains("upload /home/alice/nginx.conf", _remote.OperationsAtDispose!);
    }

    [Fact]
    public async Task Stop_ends_the_follow_up_without_touching_the_server()
    {
        var (vm, _) = await EditingAsync();

        vm.StopEditsCommand.Execute(null);

        Assert.False(vm.HasEdits);
        Assert.Equal(string.Empty, vm.EditsText);
    }

    [Fact]
    public async Task An_application_that_does_not_start_is_reported()
    {
        _launcher.Succeeds = false;

        await EditingAsync();

        Assert.Equal("Sftp_EditLaunchFailed(nginx.conf)", Assert.Single(_dialogs.Errors));
    }

    [Fact]
    public async Task A_large_file_asks_first_and_directories_are_not_editable()
    {
        _remote.AddFile("/home/alice/gros.log", new string('x', 10));
        _remote.AddDirectory("/home/alice/conf.d");
        var vm = await ConnectedAsync();
        _dialogs.ConfirmResult = false;

        await vm.EditRemoteAsync(Item(vm, "conf.d"));
        Assert.Empty(_launcher.Opened);

        var large = Item(vm, "gros.log");
        var entry = large.Remote! with { Size = SftpBrowserViewModel.LargeEditSize + 1 };
        await vm.EditRemoteAsync(FileItemViewModel.FromRemote(entry, new FileSizeFormatter(new KeyStrings())));

        Assert.Single(_dialogs.Confirmations);
        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public async Task Without_a_launcher_editing_is_not_offered()
    {
        _factory.SftpSessions.Enqueue(() => _remote);
        var vm = new SftpBrowserViewModel(_profile, _factory, _dialogs, new KeyStrings(), new ImmediateDispatcher(), initialLocalPath: _temp.Path);
        await vm.ConnectAsync();

        Assert.False(vm.CanEdit);
        await vm.EditRemoteAsync(Item(vm, "nginx.conf"));
        Assert.Empty(vm.Edits);
    }

    [Fact]
    public void Leftover_copies_are_cleaned_at_startup()
    {
        var old = _files.NewLocalPath("ancien.txt");
        File.WriteAllText(old, "reste");

        _ = new SftpSessionsViewModel(_factory, _dialogs, new KeyStrings(), new ImmediateDispatcher(), editFiles: _files);

        Assert.False(Directory.Exists(_files.Root));
    }

    // ---- Surveillance réelle du fichier ------------------------------------------------------

    [Fact]
    public async Task The_watcher_reports_a_real_save_once_and_ignores_a_touch()
    {
        var local = _files.NewLocalPath("notes.txt");
        File.WriteAllText(local, "v1");
        // Regroupement d'une seconde : les écritures d'un même enregistrement (quelques microsecondes) n'en font qu'un,
        // même sur une machine de CI chargée.
        using var edit = new RemoteEdit("/home/alice/notes.txt", local, default, TimeSpan.FromSeconds(1));
        var saves = 0;
        edit.Saved += _ => Interlocked.Increment(ref saves);
        edit.Start();

        // Même contenu réécrit : pas d'envoi.
        File.WriteAllText(local, "v1");
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        Assert.Equal(0, Volatile.Read(ref saves));

        // Enregistrement « à la VS Code » : fichier temporaire puis renommage par-dessus, en plusieurs écritures.
        var temporary = local + ".tmp";
        File.WriteAllText(temporary, "v2");
        File.Move(temporary, local, overwrite: true);
        File.AppendAllText(local, " fin");
        for (var i = 0; i < 100 && Volatile.Read(ref saves) == 0; i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        await Task.Delay(1500, TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref saves));
        Assert.True(edit.HasLocalChanges());
    }

    private sealed class FakeLauncher : IFileLauncher
    {
        public bool Succeeds { get; set; } = true;

        public List<(string Path, bool Choose)> Opened { get; } = [];

        public Task<bool> OpenAsync(string path, bool chooseApplication)
        {
            Opened.Add((path, chooseApplication));
            return Task.FromResult(Succeeds);
        }
    }
}
