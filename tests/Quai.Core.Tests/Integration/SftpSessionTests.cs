using Quai.Core.Sftp;
using Quai.Core.Ssh;
using Quai.Core.Tests.Support;

namespace Quai.Core.Tests.Integration;

/// <summary>Opérations SFTP réelles sur le serveur OpenSSH de test.</summary>
public sealed class SftpSessionTests : IAsyncLifetime
{
    private readonly TempDirectory _local = new();
    private ISftpSession _session = null!;
    private SftpTransferEngine _engine = null!;
    private string _remoteRoot = string.Empty;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(SshTestServer.Host))
        {
            return;
        }

        var prompts = new ScriptedPrompts();
        var factory = new SshSessionFactory(
            new SshConnector(new KnownHostsStore(_local.File("known_hosts")), new InMemorySecretStore(), prompts, prompts));
        _session = await factory.OpenSftpAsync(SshTestServer.KeyProfile(SshTestServer.KeyFile), Token);
        _engine = new SftpTransferEngine(_session);
        _remoteRoot = RemotePath.Combine(_session.HomeDirectory, "quai-" + Guid.NewGuid().ToString("N"));
        await _session.CreateDirectoryAsync(_remoteRoot, Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            try
            {
                await _engine.DeleteAsync(new RemoteEntry("root", _remoteRoot, true, false, 0, DateTime.Now, 0), CancellationToken.None);
            }
            catch (Exception)
            {
                // Déjà supprimé par le test.
            }

            await _session.DisposeAsync();
        }

        _local.Dispose();
    }

    private string Remote(string name) => RemotePath.Combine(_remoteRoot, name);

    private string LocalFile(string name, string content)
    {
        var path = _local.File(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<RemoteEntry> EntryAsync(string name) =>
        (await _session.ListAsync(_remoteRoot, Token)).Single(e => e.Name == name);

    [Fact]
    public async Task The_home_directory_is_absolute_and_listable()
    {
        SshTestServer.SkipUnlessAvailable();

        Assert.StartsWith("/", _session.HomeDirectory);
        Assert.True(_session.IsConnected);
        var entries = await _session.ListAsync(_session.HomeDirectory, Token);
        Assert.Contains(entries, e => e.FullPath == _remoteRoot && e.IsDirectory);
        Assert.DoesNotContain(entries, e => e.Name is "." or "..");
    }

    [Fact]
    public async Task A_file_round_trips_with_its_content_size_and_date()
    {
        SshTestServer.SkipUnlessAvailable();
        var source = LocalFile("envoi.txt", "Bonjour, quai ! é è à\n");
        var stamp = new DateTime(2024, 5, 17, 10, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, stamp);
        var uploaded = new List<long>();

        await _engine.UploadAsync(source, _remoteRoot, new Collector(uploaded), Token);

        var entry = await EntryAsync("envoi.txt");
        Assert.False(entry.IsDirectory);
        Assert.Equal(new FileInfo(source).Length, entry.Size);
        Assert.Equal(stamp, entry.LastWriteTime.ToUniversalTime());
        Assert.Equal(entry.Size, uploaded[^1]);

        var target = Directory.CreateDirectory(_local.File("retour")).FullName;
        var downloaded = new List<long>();
        await _engine.DownloadAsync(entry, target, new Collector(downloaded), Token);

        var copy = Path.Combine(target, "envoi.txt");
        Assert.Equal(File.ReadAllText(source), File.ReadAllText(copy));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(copy));
        Assert.Equal(entry.Size, downloaded[^1]);
    }

    [Fact]
    public async Task A_directory_tree_is_uploaded_measured_downloaded_and_deleted_recursively()
    {
        SshTestServer.SkipUnlessAvailable();
        LocalFile(Path.Combine("projet", "a.txt"), "aaaa");
        LocalFile(Path.Combine("projet", "sous", "b.txt"), "bbbbbbbb");
        LocalFile(Path.Combine("projet", "sous", "plus", "c.bin"), new string('c', 5000));
        Directory.CreateDirectory(_local.File(Path.Combine("projet", "vide")));
        var source = _local.File("projet");

        Assert.Equal(4 + 8 + 5000, await SftpTransferEngine.MeasureLocalAsync(source, Token));
        var progress = new List<long>();
        await _engine.UploadAsync(source, _remoteRoot, new Collector(progress), Token);

        var entry = await EntryAsync("projet");
        Assert.True(entry.IsDirectory);
        Assert.Equal(5012, await _engine.MeasureRemoteAsync(entry, Token));
        Assert.Equal(5012, progress[^1]);
        Assert.Equal(progress.Order(), progress);
        Assert.Contains(await _session.ListAsync(Remote("projet"), Token), e => e is { Name: "vide", IsDirectory: true });

        var target = Directory.CreateDirectory(_local.File("copie")).FullName;
        await _engine.DownloadAsync(entry, target, null, Token);
        Assert.Equal("bbbbbbbb", File.ReadAllText(Path.Combine(target, "projet", "sous", "b.txt")));
        Assert.Equal(5000, new FileInfo(Path.Combine(target, "projet", "sous", "plus", "c.bin")).Length);
        Assert.True(Directory.Exists(Path.Combine(target, "projet", "vide")));

        await _engine.DeleteAsync(entry, Token);
        Assert.False(await _session.ExistsAsync(Remote("projet"), Token));
    }

    [Fact]
    public async Task Folders_are_created_renamed_and_removed()
    {
        SshTestServer.SkipUnlessAvailable();

        await _session.CreateDirectoryAsync(Remote("nouveau"), Token);
        Assert.True((await EntryAsync("nouveau")).IsDirectory);

        await _session.RenameAsync(Remote("nouveau"), Remote("renommé"), Token);
        Assert.False(await _session.ExistsAsync(Remote("nouveau"), Token));
        Assert.True(await _session.ExistsAsync(Remote("renommé"), Token));

        await _session.DeleteDirectoryAsync(Remote("renommé"), Token);
        Assert.False(await _session.ExistsAsync(Remote("renommé"), Token));
    }

    [Fact]
    public async Task Permissions_are_read_and_changed()
    {
        SshTestServer.SkipUnlessAvailable();
        await _engine.UploadAsync(LocalFile("script.sh", "#!/bin/sh\n"), _remoteRoot, null, Token);

        await _session.ChangePermissionsAsync(Remote("script.sh"), 0b111_101_000, Token);
        Assert.Equal("-rwxr-x---", UnixPermissions.ToSymbolic((await EntryAsync("script.sh")).Permissions));

        await _session.ChangePermissionsAsync(Remote("script.sh"), 0b110_000_000, Token);
        Assert.Equal("600", UnixPermissions.ToOctal((await EntryAsync("script.sh")).Permissions));
    }

    [Fact]
    public async Task Uploading_over_an_existing_file_replaces_it()
    {
        SshTestServer.SkipUnlessAvailable();
        await _engine.UploadAsync(LocalFile("fichier.txt", "première version, plus longue"), _remoteRoot, null, Token);

        await _engine.UploadAsync(LocalFile("fichier.txt", "v2"), _remoteRoot, null, Token);

        Assert.Equal(2, (await EntryAsync("fichier.txt")).Size);
    }

    [Fact]
    public async Task A_cancelled_upload_stops_and_leaves_no_partial_file()
    {
        SshTestServer.SkipUnlessAvailable();
        var big = _local.File("gros.bin");
        await using (var stream = File.Create(big))
        {
            stream.SetLength(64L * 1024 * 1024);
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var progress = new CancelAfter(cancellation, threshold: 512 * 1024);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _engine.UploadAsync(big, _remoteRoot, progress, cancellation.Token));

        Assert.True(progress.Last < 64L * 1024 * 1024);
        Assert.False(await _session.ExistsAsync(Remote("gros.bin"), Token));
    }

    [Fact]
    public async Task A_cancelled_download_stops_and_leaves_no_partial_file()
    {
        SshTestServer.SkipUnlessAvailable();
        var big = _local.File("gros.bin");
        await using (var stream = File.Create(big))
        {
            stream.SetLength(64L * 1024 * 1024);
        }

        await _engine.UploadAsync(big, _remoteRoot, null, Token);
        var entry = await EntryAsync("gros.bin");
        var target = Directory.CreateDirectory(_local.File("cible")).FullName;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _engine.DownloadAsync(entry, target, new CancelAfter(cancellation, threshold: 512 * 1024), cancellation.Token));

        Assert.False(File.Exists(Path.Combine(target, "gros.bin")));
    }

    [Fact]
    public async Task A_link_to_a_directory_is_browsable_and_deleted_without_touching_its_target()
    {
        SshTestServer.SkipUnlessAvailable();
        await _session.CreateDirectoryAsync(Remote("cible"), Token);
        await _engine.UploadAsync(LocalFile("dans-cible.txt", "x"), Remote("cible"), null, Token);
        await RunRemoteAsync($"ln -s '{Remote("cible")}' '{Remote("lien")}'");

        var link = await EntryAsync("lien");
        Assert.True(link.IsSymbolicLink);
        Assert.True(link.IsDirectory);
        Assert.Single(await _session.ListAsync(link.FullPath, Token));

        await _engine.DeleteAsync(link, Token);

        Assert.DoesNotContain(await _session.ListAsync(_remoteRoot, Token), e => e.Name == "lien");
        Assert.True(await _session.ExistsAsync(Remote("cible/dans-cible.txt"), Token));
    }

    [Fact]
    public async Task A_link_to_a_file_is_renamed_and_deleted_without_touching_its_target()
    {
        SshTestServer.SkipUnlessAvailable();
        await _engine.UploadAsync(LocalFile("original.txt", "contenu précieux"), _remoteRoot, null, Token);
        await RunRemoteAsync($"ln -s '{Remote("original.txt")}' '{Remote("raccourci")}'");

        var link = await EntryAsync("raccourci");
        Assert.True(link.IsSymbolicLink);
        Assert.False(link.IsDirectory);

        await _engine.RenameAsync(link, Remote("raccourci-renommé"), Token);
        var names = (await _session.ListAsync(_remoteRoot, Token)).Select(e => e.Name).Order().ToArray();
        Assert.Equal(["original.txt", "raccourci-renommé"], names);

        var renamed = await EntryAsync("raccourci-renommé");
        Assert.True(renamed.IsSymbolicLink);
        await _engine.DeleteAsync(renamed, Token);

        var remaining = Assert.Single(await _session.ListAsync(_remoteRoot, Token));
        Assert.Equal("original.txt", remaining.Name);
        Assert.False(remaining.IsSymbolicLink);
        Assert.Equal(new FileInfo(_local.File("original.txt")).Length, remaining.Size);
    }

    [Fact]
    public async Task Listing_a_missing_directory_fails()
    {
        SshTestServer.SkipUnlessAvailable();

        await Assert.ThrowsAnyAsync<Exception>(() => _session.ListAsync(Remote("absent"), Token));
    }

    private static async Task RunRemoteAsync(string command)
    {
        var prompts = new ScriptedPrompts();
        using var temp = new TempDirectory();
        var connector = new SshConnector(new KnownHostsStore(temp.File("known_hosts")), new InMemorySecretStore(), prompts, prompts);
        using var client = await connector.ConnectAsync(
            SshTestServer.KeyProfile(SshTestServer.KeyFile), info => new Renci.SshNet.SshClient(info), Token);
        using var result = client.RunCommand(command);
        Assert.Equal(0, result.ExitStatus);
    }

    private sealed class Collector(List<long> values) : IProgress<long>
    {
        public void Report(long value)
        {
            lock (values)
            {
                values.Add(value);
            }
        }
    }

    private sealed class CancelAfter(CancellationTokenSource cancellation, long threshold) : IProgress<long>
    {
        public long Last { get; private set; }

        public void Report(long value)
        {
            Last = value;
            if (value >= threshold)
            {
                cancellation.Cancel();
            }
        }
    }
}
