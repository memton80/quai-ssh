using System.Net;
using System.Security.Cryptography;
using Quai.Core.Storage;
using Quai.Core.Tests.Support;
using Quai.Core.Updates;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.Updates;

public class UpdateDownloaderTests
{
    private static readonly byte[] Installer = [.. Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7))];
    private static readonly Uri Url = new("https://github.com/memton80/quai-ssh/releases/download/v9.0.0/Quai-Setup-x64.exe");

    [Fact]
    public async Task The_installer_is_downloaded_and_verified()
    {
        using var temp = new TempDirectory();
        var handler = new FileHandler(Installer);
        using var downloader = new UpdateDownloader(handler);
        var reports = new List<double>();

        var path = await downloader.DownloadAsync(Package(), temp.Path, new InlineProgress(reports.Add), TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(temp.Path, "Quai-Setup-x64.exe"), path);
        Assert.Equal(Installer, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(1, reports[^1]);
        Assert.True(reports.Count is > 2 and <= 101, $"{reports.Count} rapports de progression");
        Assert.Equal(Url, Assert.Single(handler.Requests).RequestUri);
        Assert.StartsWith("Quai/", handler.Requests[0].Headers.UserAgent.ToString(), StringComparison.Ordinal);
        Assert.Null(handler.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task A_file_already_downloaded_and_intact_is_not_downloaded_again()
    {
        using var temp = new TempDirectory();
        await File.WriteAllBytesAsync(temp.File("Quai-Setup-x64.exe"), Installer, TestContext.Current.CancellationToken);
        var handler = new FileHandler(Installer);
        using var downloader = new UpdateDownloader(handler);

        await downloader.DownloadAsync(Package(), temp.Path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_altered_file_is_refused_and_deleted()
    {
        using var temp = new TempDirectory();
        var altered = Installer.ToArray();
        altered[1234] ^= 0xFF;
        using var downloader = new UpdateDownloader(new FileHandler(altered));

        await Assert.ThrowsAsync<UpdateIntegrityException>(
            () => downloader.DownloadAsync(Package(), temp.Path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Theory]
    [InlineData(-1, true)]   // tronqué, sans Content-Length
    [InlineData(+1, true)]   // un octet de trop, sans Content-Length
    [InlineData(-1, false)]  // Content-Length différent de la taille annoncée par GitHub
    public async Task A_file_of_the_wrong_size_is_refused_and_deleted(int difference, bool hideLength)
    {
        using var temp = new TempDirectory();
        byte[] body = difference < 0 ? Installer[..^1] : [.. Installer, 0];
        using var downloader = new UpdateDownloader(new FileHandler(body) { HideLength = hideLength });

        await Assert.ThrowsAsync<UpdateIntegrityException>(
            () => downloader.DownloadAsync(Package(), temp.Path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task A_server_error_leaves_nothing_behind()
    {
        using var temp = new TempDirectory();
        using var downloader = new UpdateDownloader(new FileHandler(Installer) { Status = HttpStatusCode.NotFound });

        await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadAsync(Package(), temp.Path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task A_cancelled_download_leaves_nothing_behind()
    {
        using var temp = new TempDirectory();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var downloader = new UpdateDownloader(new FileHandler(Installer));
        var progress = new InlineProgress(value =>
        {
            if (value > 0.3)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => downloader.DownloadAsync(Package(), temp.Path, progress, cancellation.Token));

        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task Only_https_is_accepted()
    {
        using var temp = new TempDirectory();
        using var downloader = new UpdateDownloader(new FileHandler(Installer));
        var package = Package() with { DownloadUrl = new Uri("http://github.com/memton80/quai-ssh/releases/download/v9.0.0/Quai-Setup-x64.exe") };

        await Assert.ThrowsAsync<ArgumentException>(
            () => downloader.DownloadAsync(package, temp.Path, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Leftovers_of_a_previous_update_are_cleaned_up()
    {
        using var temp = new TempDirectory();
        var updates = Path.Combine(temp.Path, "updates");
        Directory.CreateDirectory(updates);
        await File.WriteAllTextAsync(Path.Combine(updates, "Quai-Setup-x64.exe"), "ancien", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(updates, "Quai-Setup-x64.exe.partial"), "inachevé", TestContext.Current.CancellationToken);

        UpdateDownloader.CleanUp(updates);
        UpdateDownloader.CleanUp(Path.Combine(temp.Path, "absent"));

        Assert.Empty(Directory.GetFiles(updates));
    }

    [Fact]
    public async Task One_click_downloads_verifies_then_hands_over_to_the_installer()
    {
        using var temp = new TempDirectory();
        var installer = new FakeUpdateInstaller();
        var banner = Banner(temp, new FileHandler(Installer), installer);
        await banner.CheckAsync(TestContext.Current.CancellationToken);
        Assert.True(banner.IsOpen);
        Assert.True(banner.CanInstallInPlace);
        Assert.Equal("Update_Install", banner.ActionText);

        await banner.ActAsync();

        var path = Assert.Single(installer.Installed);
        Assert.Equal(Path.Combine(temp.Path, "updates", "Quai-Setup-x64.exe"), path);
        Assert.Equal(Installer, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal("Update_Installing(9.0.0)", banner.Message);
        Assert.False(banner.HasFailed);
        Assert.Empty(installer.OpenedPages);
    }

    [Fact]
    public async Task Keeping_the_sessions_open_postpones_the_update_without_downloading_again()
    {
        using var temp = new TempDirectory();
        var handler = new FileHandler(Installer);
        var installer = new FakeUpdateInstaller { Accept = false };
        var banner = Banner(temp, handler, installer);
        await banner.CheckAsync(TestContext.Current.CancellationToken);

        await banner.ActAsync();
        Assert.Equal("Update_Ready(9.0.0)", banner.Message);
        Assert.Equal("Update_Install", banner.ActionText);
        Assert.True(banner.IsActionEnabled);

        installer.Accept = true;
        await banner.ActAsync();

        Assert.Single(handler.Requests);
        Assert.Single(installer.Installed);
    }

    [Fact]
    public async Task An_altered_download_is_reported_and_can_be_retried()
    {
        using var temp = new TempDirectory();
        var altered = Installer.ToArray();
        altered[0] ^= 0xFF;
        var handler = new FileHandler(altered);
        var installer = new FakeUpdateInstaller();
        var banner = Banner(temp, handler, installer);
        await banner.CheckAsync(TestContext.Current.CancellationToken);

        await banner.ActAsync();

        Assert.True(banner.HasFailed);
        Assert.Equal("Update_Failed(9.0.0,Update_FailedIntegrity)", banner.Message);
        Assert.Equal("Update_Retry", banner.ActionText);
        Assert.Empty(installer.Installed);

        handler.Body = Installer;
        await banner.ActAsync();

        Assert.False(banner.HasFailed);
        Assert.Single(installer.Installed);
    }

    [Fact]
    public async Task A_network_or_launch_failure_says_which_step_failed()
    {
        using var temp = new TempDirectory();
        var installer = new FakeUpdateInstaller();
        var handler = new FileHandler(Installer) { Status = HttpStatusCode.BadGateway };
        var banner = Banner(temp, handler, installer);
        await banner.CheckAsync(TestContext.Current.CancellationToken);

        await banner.ActAsync();
        Assert.Equal("Update_Failed(9.0.0,Update_FailedDownload)", banner.Message);

        handler.Status = HttpStatusCode.OK;
        installer.Failure = new InvalidOperationException("installateur introuvable");
        await banner.ActAsync();
        Assert.Equal("Update_Failed(9.0.0,Update_FailedInstall)", banner.Message);
        Assert.True(banner.IsActionEnabled);
    }

    [Fact]
    public async Task The_same_button_cancels_a_download_in_progress()
    {
        using var temp = new TempDirectory();
        var handler = new FileHandler(Installer) { Gate = new TaskCompletionSource() };
        var installer = new FakeUpdateInstaller();
        var banner = Banner(temp, handler, installer);
        await banner.CheckAsync(TestContext.Current.CancellationToken);

        var update = banner.ActAsync();
        Assert.True(banner.IsDownloading);
        Assert.Equal("Update_Cancel", banner.ActionText);
        Assert.Equal("Update_Downloading(9.0.0)", banner.Message);
        await banner.ActAsync();
        await update;

        Assert.False(banner.IsDownloading);
        Assert.False(banner.HasFailed);
        Assert.Equal("Update_Install", banner.ActionText);
        Assert.StartsWith("Update_Message(9.0.0,", banner.Message, StringComparison.Ordinal);
        Assert.Empty(installer.Installed);
        Assert.Empty(Directory.GetFiles(Path.Combine(temp.Path, "updates")));
    }

    [Fact]
    public async Task Closing_the_banner_cancels_the_download()
    {
        using var temp = new TempDirectory();
        var handler = new FileHandler(Installer) { Gate = new TaskCompletionSource() };
        var installer = new FakeUpdateInstaller();
        var banner = Banner(temp, handler, installer);
        await banner.CheckAsync(TestContext.Current.CancellationToken);

        var update = banner.ActAsync();
        banner.IsOpen = false;
        await update;

        Assert.Empty(installer.Installed);
        Assert.False(banner.IsDownloading);
    }

    [Theory]
    [InlineData(false, true)]   // copie portable (zip)
    [InlineData(true, false)]   // release sans installateur vérifiable
    public async Task Otherwise_the_button_opens_the_release_page(bool installed, bool withInstaller)
    {
        using var temp = new TempDirectory();
        var handler = new FileHandler(Installer);
        var installer = new FakeUpdateInstaller { CanInstall = installed };
        var banner = Banner(temp, handler, installer, withInstaller);
        await banner.CheckAsync(TestContext.Current.CancellationToken);
        Assert.False(banner.CanInstallInPlace);
        Assert.Equal("Update_Download", banner.ActionText);

        await banner.ActAsync();

        Assert.Equal("https://github.com/memton80/quai-ssh/releases/tag/v9.0.0", Assert.Single(installer.OpenedPages).ToString());
        Assert.Empty(installer.Installed);
        Assert.Empty(handler.Requests);
    }

    private static UpdatePackage Package() =>
        new("Quai-Setup-x64.exe", Url, Installer.Length, Convert.ToHexStringLower(SHA256.HashData(Installer)));

    private static UpdateBannerViewModel Banner(TempDirectory temp, FileHandler handler, FakeUpdateInstaller installer, bool withInstaller = true)
    {
        ReleaseAsset[] assets = withInstaller
            ? [new ReleaseAsset("Quai-Setup-x64.exe", Installer.Length, Convert.ToHexStringLower(SHA256.HashData(Installer)))]
            : [];
        var paths = new AppPaths(temp.Path);
        return new UpdateBannerViewModel(
            new UpdateChecker(new Source(new ReleaseInfo("v9.0.0", assets)), "1.0.0", "Quai-Setup-x64.exe"),
            new UpdateDownloader(handler),
            installer,
            new JsonSettingsService(paths),
            new KeyStrings(),
            paths);
    }

    private sealed class Source(ReleaseInfo release) : IReleaseSource
    {
        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default) => Task.FromResult<ReleaseInfo?>(release);
    }

    /// <summary>Progression signalée sur-le-champ (sans contexte de synchronisation).</summary>
    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>Sert l'installateur par morceaux ; <see cref="Gate"/> retient le téléchargement après le premier morceau.</summary>
    private sealed class FileHandler(byte[] body) : HttpMessageHandler
    {
        public byte[] Body { get; set; } = body;

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public bool HideLength { get; init; }

        public TaskCompletionSource? Gate { get; init; }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Status != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(Status));
            }

            var content = new StreamContent(new ChunkedStream(Body, Gate));
            if (!HideLength)
            {
                content.Headers.ContentLength = Body.Length;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class ChunkedStream(byte[] data, TaskCompletionSource? gate) : Stream
    {
        private const int Chunk = 8192;
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (gate is not null && _position > 0)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            await Task.Yield();
            var count = Math.Min(Math.Min(buffer.Length, Chunk), data.Length - _position);
            data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
