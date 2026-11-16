using System.Net;
using System.Text;
using Quai.Core.Models;
using Quai.Core.Storage;
using Quai.Core.Tests.Support;
using Quai.Core.Updates;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.Updates;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.0", "1.1.9", "1.2.0")]
    [InlineData("v0.2.0", "0.1.0", "0.2.0")]
    [InlineData("v0.1.0", "0.0.0-ci.9", "0.1.0")]
    [InlineData("v1.0.0", "1.0.0-ci.41", "1.0.0")] // version du CI construite avant la release
    [InlineData("1.10.0", "1.9.3", "1.10.0")]     // ordre numérique, pas alphabétique
    [InlineData(" v2.0.0-rc.1 ", "1.5.0", "2.0.0-rc.1")]
    public async Task A_newer_release_is_offered(string tag, string current, string expected)
    {
        var update = await new UpdateChecker(new FixedSource(tag), current).CheckAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Equal(expected, update.Version);
        Assert.Equal($"https://github.com/memton80/quai-ssh/releases/tag/{tag.Trim()}", update.ReleasePage.ToString());
    }

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("v1.2.0", "1.3.0")]
    [InlineData("v1.2.0-rc.1", "1.2.0")]
    [InlineData(null, "1.0.0")]                    // aucune release publiée
    [InlineData("nightly", "1.0.0")]               // étiquette qui n'est pas une version
    [InlineData("v1.2.0", "version inconnue")]
    public async Task Nothing_is_offered_when_up_to_date_or_unsure(string? tag, string current)
    {
        var update = await new UpdateChecker(new FixedSource(tag), current).CheckAsync(TestContext.Current.CancellationToken);

        Assert.Null(update);
    }

    [Fact]
    public async Task The_github_source_reads_the_latest_tag_without_any_credential()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{ "tag_name": "v3.1.4", "html_url": "https://example.org/piege", "draft": false }""");
        using var source = new GitHubReleaseSource(handler);

        var release = await source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal("v3.1.4", release?.Tag);
        Assert.Empty(release!.Assets);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.github.com/repos/memton80/quai-ssh/releases/latest", request.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.StartsWith("Quai/", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task The_github_source_reads_each_file_with_its_size_and_sha256()
    {
        var body = $$"""
            {
              "tag_name": "v1.1.0",
              "assets": [
                { "name": "Quai-Setup-x64.exe", "size": 35637956, "digest": "sha256:{{Hash('A')}}",
                  "browser_download_url": "https://example.org/piege.exe" },
                { "name": "Quai-x64.zip", "size": 49416463, "digest": null },
                { "name": "Quai-Setup-arm64.exe", "size": 34610396, "digest": "md5:0123" },
                { "name": "sans-taille.exe" },
                "pas un objet"
              ]
            }
            """;
        using var source = new GitHubReleaseSource(new RecordingHandler(HttpStatusCode.OK, body));

        var release = await source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(release);
        Assert.Equal(
            new[]
            {
                new ReleaseAsset("Quai-Setup-x64.exe", 35637956, Hash('a')),
                new ReleaseAsset("Quai-x64.zip", 49416463, null),
                new ReleaseAsset("Quai-Setup-arm64.exe", 34610396, null),
            },
            release.Assets);
    }

    [Theory]
    [InlineData(System.Runtime.InteropServices.Architecture.X64, "Quai-Setup-x64.exe")]
    [InlineData(System.Runtime.InteropServices.Architecture.Arm64, "Quai-Setup-arm64.exe")]
    [InlineData(System.Runtime.InteropServices.Architecture.X86, null)]
    public void Each_architecture_has_its_installer(System.Runtime.InteropServices.Architecture architecture, string? expected)
    {
        Assert.Equal(expected, UpdateChecker.InstallerNameFor(architecture));
    }

    [Fact]
    public async Task The_installer_is_offered_with_an_address_built_by_the_application()
    {
        var source = new FixedSource(new ReleaseInfo("v1.1.0",
        [
            new ReleaseAsset("Quai-Setup-arm64.exe", 100, Hash('b')),
            new ReleaseAsset("Quai-Setup-x64.exe", 200, Hash('c')),
        ]));

        var update = await new UpdateChecker(source, "1.0.2", "Quai-Setup-x64.exe").CheckAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(update?.Installer);
        Assert.Equal("Quai-Setup-x64.exe", update.Installer.FileName);
        Assert.Equal("https://github.com/memton80/quai-ssh/releases/download/v1.1.0/Quai-Setup-x64.exe", update.Installer.DownloadUrl.ToString());
        Assert.Equal(200, update.Installer.Size);
        Assert.Equal(Hash('c'), update.Installer.Sha256);
    }

    [Theory]
    [InlineData("Quai-Setup-x64.exe", 200L, null)]                    // sans empreinte : rien pour vérifier le fichier
    [InlineData("Quai-Setup-x64.exe", 0L, "c")]                       // taille absurde
    [InlineData("Quai-Setup-x64.exe", 600L * 1024 * 1024, "c")]       // bien trop gros pour être Quai
    [InlineData("quai-setup-x64.exe", 200L, "c")]                     // pas exactement le nom attendu
    [InlineData("Quai-x64.zip", 200L, "c")]
    public async Task Without_a_verifiable_installer_only_the_release_page_is_offered(string name, long size, string? hash)
    {
        var source = new FixedSource(new ReleaseInfo("v1.1.0", [new ReleaseAsset(name, size, hash is null ? null : Hash(hash[0]))]));

        var update = await new UpdateChecker(source, "1.0.2", "Quai-Setup-x64.exe").CheckAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Null(update.Installer);
        Assert.Equal("https://github.com/memton80/quai-ssh/releases/tag/v1.1.0", update.ReleasePage.ToString());
    }

    [Fact]
    public async Task No_release_yet_is_not_an_error()
    {
        using var source = new GitHubReleaseSource(new RecordingHandler(HttpStatusCode.NotFound, """{ "message": "Not Found" }"""));

        Assert.Null(await source.GetLatestAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{ "message": "API rate limit exceeded" }""")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    public async Task A_github_error_is_reported_to_the_caller(HttpStatusCode status, string body)
    {
        using var source = new GitHubReleaseSource(new RecordingHandler(status, body));

        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetLatestAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("""{ "name": "sans étiquette" }""")]
    [InlineData("""{ "tag_name": 12 }""")]
    [InlineData("[]")]
    public async Task An_unexpected_answer_gives_no_tag(string body)
    {
        using var source = new GitHubReleaseSource(new RecordingHandler(HttpStatusCode.OK, body));

        Assert.Null(await source.GetLatestAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_banner_opens_with_the_new_version_and_its_release_page()
    {
        using var temp = new TempDirectory();
        var banner = Banner(new FixedSource("v99.0.0"), temp);

        await banner.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(banner.IsOpen);
        Assert.Equal($"Update_Message(99.0.0,{AppInfo.Version})", banner.Message);
        Assert.Equal("https://github.com/memton80/quai-ssh/releases/tag/v99.0.0", banner.ReleasePage!.ToString());
    }

    [Fact]
    public async Task The_banner_stays_closed_when_up_to_date_or_offline()
    {
        using var temp = new TempDirectory();
        var upToDate = Banner(new FixedSource("v0.0.1"), temp);
        var offline = Banner(new FailingSource(new HttpRequestException("pas de réseau")), temp);
        var timeout = Banner(new FailingSource(new TaskCanceledException("délai dépassé")), temp);

        await upToDate.CheckAsync(TestContext.Current.CancellationToken);
        await offline.CheckAsync(TestContext.Current.CancellationToken);
        await timeout.CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(upToDate.IsOpen);
        Assert.False(offline.IsOpen);
        Assert.False(timeout.IsOpen);
    }

    [Fact]
    public async Task Nothing_is_asked_to_github_when_the_setting_is_off()
    {
        using var temp = new TempDirectory();
        var settings = new JsonSettingsService(temp.File("settings.json"));
        settings.Update(s => s.CheckForUpdates = false);
        var source = new FixedSource("v99.0.0");
        using var downloader = new UpdateDownloader();
        var banner = new UpdateBannerViewModel(
            new UpdateChecker(source, "1.0.0"), downloader, new FakeUpdateInstaller(), settings, new KeyStrings(), new AppPaths(temp.Path));

        await banner.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, source.Calls);
        Assert.False(banner.IsOpen);
    }

    [Fact]
    public void The_setting_is_on_by_default_and_saved_from_the_settings_page()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        var service = new JsonSettingsService(paths);
        var vm = new SettingsViewModel(service, paths);
        Assert.True(service.Current.CheckForUpdates);
        Assert.True(vm.CheckForUpdates);

        vm.CheckForUpdates = false;

        Assert.False(new JsonSettingsService(paths).Current.CheckForUpdates);
        Assert.True(new AppSettings().CheckForUpdates);
    }

    private static UpdateBannerViewModel Banner(IReleaseSource source, TempDirectory temp) =>
        new(
            new UpdateChecker(source, AppInfo.Version),
            new UpdateDownloader(),
            new FakeUpdateInstaller(),
            new JsonSettingsService(temp.File("settings.json")),
            new KeyStrings(),
            new AppPaths(temp.Path));

    private static string Hash(char digit) => new(digit, 64);

    private sealed class FixedSource(ReleaseInfo? release) : IReleaseSource
    {
        public FixedSource(string? tag)
            : this(tag is null ? null : new ReleaseInfo(tag, []))
        {
        }

        public int Calls { get; private set; }

        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(release);
        }
    }

    private sealed class FailingSource(Exception error) : IReleaseSource
    {
        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default) => Task.FromException<ReleaseInfo?>(error);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
