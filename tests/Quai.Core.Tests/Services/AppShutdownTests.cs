using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.Services;

public sealed class AppShutdownTests : IDisposable
{
    private readonly TempDirectory _local = new();
    private readonly FakeSessionFactory _factory = new();
    private readonly ConnectionProfile _profile = new() { Name = "Serveur", Host = "example.org", Username = "alice" };
    private readonly TerminalsViewModel _terminals;
    private readonly SftpSessionsViewModel _sftp;
    private readonly AppShutdown _shutdown;

    public AppShutdownTests()
    {
        _terminals = new TerminalsViewModel(_factory, new KeyStrings(), new ImmediateDispatcher());
        _sftp = new SftpSessionsViewModel(_factory, new FakeDialogService(), new KeyStrings(), new ImmediateDispatcher());
        _shutdown = new AppShutdown(_terminals, _sftp, new KeyStrings());
    }

    public void Dispose() => _local.Dispose();

    [Fact]
    public void Nothing_open_means_nothing_to_confirm()
    {
        Assert.True(_shutdown.Inventory().IsEmpty);
    }

    [Fact]
    public async Task Only_live_sessions_and_active_transfers_are_counted()
    {
        var live = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => live);
        _factory.Terminals.Enqueue(() => throw new InvalidOperationException("injoignable"));
        var connected = _terminals.Open(_profile);
        var failed = _terminals.Open(_profile);
        await connected.ConnectAsync(80, 24);
        await failed.ConnectAsync(80, 24);
        _terminals.Open(_profile); // jamais affiché : aucune connexion lancée

        var remote = new FakeSftpSession { TransferGate = new TaskCompletionSource() };
        _factory.SftpSessions.Enqueue(() => remote);
        var browser = _sftp.Open(_profile);
        await WaitForAsync(() => browser.IsConnected);
        var file = _local.File("gros.bin");
        await File.WriteAllTextAsync(file, new string('x', 1000), TestContext.Current.CancellationToken);
        await browser.UploadAsync([file, file]);

        var open = _shutdown.Inventory();

        Assert.Equal(new OpenSessions(1, 1, 2), open);
        Assert.False(open.IsEmpty);
        remote.TransferGate.SetResult();
    }

    [Theory]
    [InlineData(1, 0, 0, "Shutdown_SessionsOpen(Shutdown_OneTerminal)")]
    [InlineData(3, 0, 0, "Shutdown_SessionsOpen(Shutdown_Terminals(3))")]
    [InlineData(0, 2, 0, "Shutdown_SessionsOpen(Shutdown_Sftp(2))")]
    [InlineData(2, 1, 0, "Shutdown_SessionsOpen(Shutdown_Both(Shutdown_Terminals(2),Shutdown_OneSftp))")]
    [InlineData(0, 1, 1, "Shutdown_SessionsOpen(Shutdown_OneSftp)|Shutdown_OneTransfer")]
    [InlineData(0, 1, 4, "Shutdown_SessionsOpen(Shutdown_OneSftp)|Shutdown_Transfers(4)")]
    public void The_confirmation_lists_what_will_be_closed(int terminals, int sftp, int transfers, string expected)
    {
        var text = _shutdown.Describe(new OpenSessions(terminals, sftp, transfers));

        Assert.Equal(expected.Replace("|", Environment.NewLine + Environment.NewLine, StringComparison.Ordinal), text);
    }

    [Fact]
    public async Task Closing_everything_disconnects_each_session_and_cleans_up_transfers()
    {
        var first = new FakeTerminalSession();
        var second = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => first);
        _factory.Terminals.Enqueue(() => second);
        await _terminals.Open(_profile).ConnectAsync(80, 24);
        await _terminals.Open(_profile).ConnectAsync(80, 24);

        var remote = new FakeSftpSession { TransferGate = new TaskCompletionSource() };
        _factory.SftpSessions.Enqueue(() => remote);
        var browser = _sftp.Open(_profile);
        await WaitForAsync(() => browser.IsConnected);
        var file = _local.File("partiel.bin");
        await File.WriteAllTextAsync(file, new string('x', 1000), TestContext.Current.CancellationToken);
        await browser.UploadAsync([file]);
        await WaitForAsync(() => browser.Transfers[0].TransferredBytes > 0);

        var completed = await _shutdown.CloseAllAsync(TimeSpan.FromSeconds(10));

        Assert.True(completed);
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
        Assert.True(remote.Disposed);
        Assert.Empty(_terminals.Tabs);
        Assert.Empty(_sftp.Tabs);
        Assert.True(_terminals.IsEmpty);
        Assert.True(_sftp.IsEmpty);
        Assert.Equal(TransferStatus.Cancelled, browser.Transfers[0].Status);

        // Le fichier incomplet a été supprimé sur le serveur avant la déconnexion.
        Assert.Contains("delete /home/alice/partiel.bin", remote.OperationsAtDispose!);
    }

    [Fact]
    public async Task A_server_that_no_longer_answers_does_not_block_the_exit()
    {
        var stuck = new FakeTerminalSession { DisposeGate = new TaskCompletionSource() };
        _factory.Terminals.Enqueue(() => stuck);
        await _terminals.Open(_profile).ConnectAsync(80, 24);

        var completed = await _shutdown.CloseAllAsync(TimeSpan.FromMilliseconds(200));

        Assert.False(completed);
        Assert.Empty(_terminals.Tabs);
        stuck.DisposeGate.SetResult();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition non atteinte dans le délai.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
