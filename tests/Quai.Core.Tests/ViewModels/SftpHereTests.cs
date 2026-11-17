using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.ViewModels;

public class SftpHereTests
{
    private readonly FakeSessionFactory _factory = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly RecordingNavigator _navigator = new();
    private readonly FakeSftpSession _remote = new();
    private readonly ConnectionProfile _profile = new() { Name = "Serveur", Host = "example.org", Username = "alice" };

    public SftpHereTests()
    {
        _remote.AddDirectory("/var");
        _remote.AddDirectory("/var/log");
        _remote.AddFile("/var/log/syslog", "journal");
    }

    private (SessionLauncher Launcher, TerminalsViewModel Terminals, SftpSessionsViewModel Sftp) Create()
    {
        var terminals = new TerminalsViewModel(_factory, new KeyStrings(), new ImmediateDispatcher());
        var sftp = new SftpSessionsViewModel(_factory, _dialogs, new KeyStrings(), new ImmediateDispatcher());
        return (new SessionLauncher(terminals, sftp, _navigator), terminals, sftp);
    }

    private static async Task<TerminalTabViewModel> ConnectedAsync(TerminalsViewModel terminals, ConnectionProfile profile)
    {
        var tab = terminals.Open(profile);
        await tab.ConnectAsync(80, 24);
        Assert.True(tab.IsConnected);
        return tab;
    }

    private static async Task WaitConnectedAsync(SftpBrowserViewModel browser)
    {
        for (var i = 0; i < 100 && !browser.IsConnected; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(browser.IsConnected);
    }

    [Fact]
    public async Task The_directory_announced_by_the_shell_is_used_first()
    {
        var session = new FakeTerminalSession { WorkingDirectory = "/tmp" };
        _factory.Terminals.Enqueue(() => session);
        _factory.SftpSessions.Enqueue(() => _remote);
        var (launcher, terminals, sftp) = Create();
        var tab = await ConnectedAsync(terminals, _profile);

        session.Emit("\u001b]7;file://example.org/var/log\u0007alice@example:/var/log$ ");
        await launcher.OpenSftpHereAsync(tab);

        var browser = Assert.Single(sftp.Tabs);
        await WaitConnectedAsync(browser);
        Assert.Equal("/var/log", browser.RemotePath);
        Assert.Contains(browser.RemoteItems, i => i.Name == "syslog");
        Assert.Equal(0, session.WorkingDirectoryQueries);
        Assert.Equal([AppPage.Sftp], _navigator.Pages);
        Assert.Equal(_profile.Id, browser.Profile.Id);
    }

    [Fact]
    public async Task Without_announcement_the_session_is_asked()
    {
        var session = new FakeTerminalSession { WorkingDirectory = "/var" };
        _factory.Terminals.Enqueue(() => session);
        _factory.SftpSessions.Enqueue(() => _remote);
        var (launcher, terminals, sftp) = Create();
        var tab = await ConnectedAsync(terminals, _profile);

        await launcher.OpenSftpHereAsync(tab);

        var browser = Assert.Single(sftp.Tabs);
        await WaitConnectedAsync(browser);
        Assert.Equal("/var", browser.RemotePath);
        Assert.Equal(1, session.WorkingDirectoryQueries);
    }

    [Fact]
    public async Task An_unknown_or_unreadable_directory_falls_back_to_home()
    {
        _factory.Terminals.Enqueue(() => new FakeTerminalSession { WorkingDirectory = "/root/secret" });
        _factory.SftpSessions.Enqueue(() => _remote);
        var (launcher, terminals, sftp) = Create();
        var tab = await ConnectedAsync(terminals, _profile);

        await launcher.OpenSftpHereAsync(tab);

        var browser = Assert.Single(sftp.Tabs);
        await WaitConnectedAsync(browser);
        Assert.Equal(_remote.HomeDirectory, browser.RemotePath);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task A_connected_explorer_of_the_same_connection_is_reused()
    {
        var session = new FakeTerminalSession { WorkingDirectory = "/var/log" };
        _factory.Terminals.Enqueue(() => session);
        _factory.SftpSessions.Enqueue(() => _remote);
        var (launcher, terminals, sftp) = Create();
        var existing = sftp.Open(_profile);
        await WaitConnectedAsync(existing);
        Assert.Equal(_remote.HomeDirectory, existing.RemotePath);
        var tab = await ConnectedAsync(terminals, _profile);

        await launcher.OpenSftpHereAsync(tab);

        Assert.Same(existing, Assert.Single(sftp.Tabs));
        Assert.Same(existing, sftp.SelectedTab);
        Assert.Equal("/var/log", existing.RemotePath);
        Assert.Single(_factory.SftpRequests);
    }

    [Fact]
    public async Task A_disconnected_terminal_still_opens_the_explorer_at_home()
    {
        var session = new FakeTerminalSession { WorkingDirectory = "/var/log" };
        _factory.Terminals.Enqueue(() => session);
        _factory.SftpSessions.Enqueue(() => _remote);
        var (launcher, terminals, sftp) = Create();
        var tab = await ConnectedAsync(terminals, _profile);
        session.Close();

        await launcher.OpenSftpHereAsync(tab);

        var browser = Assert.Single(sftp.Tabs);
        await WaitConnectedAsync(browser);
        Assert.Equal(_remote.HomeDirectory, browser.RemotePath);
        Assert.Equal(0, session.WorkingDirectoryQueries);
    }

    [Fact]
    public async Task A_serial_terminal_has_no_explorer()
    {
        var serial = new ConnectionProfile { Protocol = ConnectionProtocol.Serial, Serial = new SerialSettings { PortName = "COM3" } };
        _factory.Terminals.Enqueue(() => new FakeTerminalSession());
        var (launcher, terminals, sftp) = Create();
        var tab = terminals.Open(serial);

        await launcher.OpenSftpHereAsync(tab);

        Assert.Empty(sftp.Tabs);
        Assert.Empty(_navigator.Pages);
    }
}
