using System.Text;
using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Ssh;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.ViewModels;

public class TerminalViewModelTests
{
    private readonly FakeSessionFactory _factory = new();
    private readonly ConnectionProfile _profile = new() { Name = "Serveur", Host = "example.org", Username = "alice" };

    private TerminalTabViewModel CreateTab(out StringBuilder output)
    {
        var tab = new TerminalTabViewModel(_profile, _factory, new KeyStrings(), new ImmediateDispatcher());
        var text = new StringBuilder();
        tab.Output += data => text.Append(Encoding.UTF8.GetString(data));
        output = text;
        return tab;
    }

    [Fact]
    public async Task Connect_opens_the_session_with_the_terminal_size_and_relays_output()
    {
        var session = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => session);
        var tab = CreateTab(out var output);

        await tab.ConnectAsync(120, 40);
        session.Emit("bonjour");

        Assert.Equal(TerminalStatus.Connected, tab.Status);
        Assert.True(tab.IsConnected);
        Assert.True(session.Started);
        Assert.Equal((120, 40), (_factory.TerminalRequests[0].Columns, _factory.TerminalRequests[0].Rows));
        Assert.Contains("Terminal_Connecting(alice@example.org)", output.ToString());
        Assert.EndsWith("bonjour", output.ToString());
    }

    [Fact]
    public async Task Input_is_sent_as_utf8_and_binary_as_single_bytes()
    {
        var session = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => session);
        var tab = CreateTab(out _);
        await tab.ConnectAsync(80, 24);

        tab.SendInput("é\r");
        tab.SendBinary("\u00ff\u0001");

        Assert.Equal([0xC3, 0xA9, 0x0D], session.Sent[0]);
        Assert.Equal([0xFF, 0x01], session.Sent[1]);
    }

    [Fact]
    public async Task Resize_is_forwarded_and_remembered_for_reconnection()
    {
        var first = new FakeTerminalSession();
        var second = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => first);
        _factory.Terminals.Enqueue(() => second);
        var tab = CreateTab(out _);
        await tab.ConnectAsync(80, 24);

        tab.Resize(100, 30);
        tab.Resize(0, 0);
        first.Close();
        await tab.ReconnectCommand.ExecuteAsync(null);

        Assert.Contains((100, 30), first.Sizes);
        Assert.DoesNotContain((0, 0), first.Sizes);
        Assert.Equal((100, 30), (_factory.TerminalRequests[1].Columns, _factory.TerminalRequests[1].Rows));
        Assert.Equal(TerminalStatus.Connected, tab.Status);
    }

    [Fact]
    public async Task A_closed_session_marks_the_tab_disconnected_and_allows_reconnection()
    {
        var session = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => session);
        var tab = CreateTab(out var output);
        await tab.ConnectAsync(80, 24);

        session.Close();

        Assert.Equal(TerminalStatus.Disconnected, tab.Status);
        Assert.True(tab.CanReconnect);
        Assert.True(session.Disposed);
        Assert.Contains("Terminal_Closed", output.ToString());
    }

    [Fact]
    public async Task A_lost_connection_reports_the_error()
    {
        var session = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => session);
        var tab = CreateTab(out _);
        await tab.ConnectAsync(80, 24);

        session.Close(new IOException("réseau coupé"));

        Assert.Equal("Terminal_ConnectionLost(réseau coupé)", tab.StatusText);
    }

    public static TheoryData<Exception, TerminalStatus, string> Failures => new()
    {
        { new OperationCanceledException(), TerminalStatus.Disconnected, "Terminal_Cancelled" },
        { new HostKeyRejectedException(new HostKeyInfo("h", 22, "t", [1]), HostKeyStatus.Changed), TerminalStatus.Failed, "Terminal_HostKeyRejected" },
        { new SshLoginFailedException("refus"), TerminalStatus.Failed, "Terminal_AuthenticationFailed" },
        { new PrivateKeyUnavailableException("C:\\cle"), TerminalStatus.Failed, "Terminal_KeyUnavailable(C:\\cle)" },
        { new InvalidOperationException("hôte injoignable"), TerminalStatus.Failed, "Terminal_ConnectFailed(hôte injoignable)" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Connection_failures_are_explained_in_the_tab(Exception error, TerminalStatus expectedStatus, string expectedText)
    {
        _factory.Terminals.Enqueue(() => throw error);
        var tab = CreateTab(out var output);

        await tab.ConnectAsync(80, 24);

        Assert.Equal(expectedStatus, tab.Status);
        Assert.Equal(expectedText, tab.StatusText);
        Assert.Contains(expectedText, output.ToString());
        Assert.True(tab.CanReconnect);
    }

    [Fact]
    public async Task Disposing_the_tab_closes_the_session()
    {
        var session = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => session);
        var tab = CreateTab(out _);
        await tab.ConnectAsync(80, 24);

        await tab.DisposeAsync();
        session.Close();

        Assert.True(session.Disposed);
        Assert.Equal(TerminalStatus.Connected, tab.Status);
    }

    [Fact]
    public async Task Tabs_are_added_selected_and_closed()
    {
        var session = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => session);
        var terminals = new TerminalsViewModel(_factory, new KeyStrings(), new ImmediateDispatcher());
        Assert.True(terminals.IsEmpty);

        var first = terminals.Open(_profile);
        var second = terminals.Open(_profile);
        await first.ConnectAsync(80, 24);

        Assert.Equal(2, terminals.Tabs.Count);
        Assert.Same(second, terminals.SelectedTab);
        Assert.False(terminals.IsEmpty);

        await terminals.CloseAsync(second);
        Assert.Same(first, terminals.SelectedTab);

        await terminals.CloseAsync(first);
        Assert.True(terminals.IsEmpty);
        Assert.Null(terminals.SelectedTab);
        Assert.True(session.Disposed);
    }

    [Fact]
    public void The_launcher_opens_a_tab_and_shows_the_terminals_page()
    {
        var terminals = new TerminalsViewModel(_factory, new KeyStrings(), new ImmediateDispatcher());
        var navigator = new RecordingNavigator();
        var sftp = new SftpSessionsViewModel(_factory, new FakeDialogService(), new KeyStrings(), new ImmediateDispatcher());
        var launcher = new SessionLauncher(terminals, sftp, navigator);

        launcher.OpenTerminal(_profile);

        Assert.Equal("Serveur", Assert.Single(terminals.Tabs).Title);
        Assert.Equal([AppPage.Terminals], navigator.Pages);
    }
}
