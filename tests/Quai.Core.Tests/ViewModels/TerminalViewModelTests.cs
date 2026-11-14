using System.Text;
using Quai.Core.Models;
using Quai.Core.Serial;
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

    private static readonly ConnectionProfile SerialProfile = new()
    {
        Name = "Console",
        Protocol = ConnectionProtocol.Serial,
        Serial = new SerialSettings { PortName = "COM3", BaudRate = 115200 },
    };

    private TerminalTabViewModel CreateSerialTab(FakeSerialSessionFactory serial, out StringBuilder output)
    {
        var tab = new TerminalTabViewModel(
            SerialProfile,
            new Quai.Core.Terminal.TerminalSessionFactory(_factory, serial),
            new KeyStrings(),
            new ImmediateDispatcher());
        var text = new StringBuilder();
        tab.Output += data => text.Append(Encoding.UTF8.GetString(data));
        output = text;
        return tab;
    }

    [Fact]
    public async Task A_serial_tab_opens_the_port_and_invites_to_press_enter()
    {
        var serial = new FakeSerialSessionFactory();
        var session = new FakeTerminalSession { CanSendBreak = true };
        serial.Sessions.Enqueue(() => session);
        var tab = CreateSerialTab(serial, out var output);

        await tab.ConnectAsync(80, 24);

        Assert.True(tab.IsSerial);
        Assert.True(tab.IsConnected);
        Assert.True(session.Started);
        Assert.Empty(_factory.TerminalRequests);
        Assert.Equal("COM3", Assert.Single(serial.Requests).PortName);
        Assert.Contains("Terminal_SerialOpening(COM3 · 115200 8N1)", output.ToString());
        Assert.Contains("Terminal_SerialOpened(COM3)", output.ToString());
    }

    public static TheoryData<SerialPortProblem, string> SerialFailures => new()
    {
        { SerialPortProblem.NotFound, "Terminal_SerialNotFound(COM3)" },
        { SerialPortProblem.InUse, "Terminal_SerialInUse(COM3)" },
        { SerialPortProblem.SettingsRejected, "Terminal_SerialSettingsRejected(COM3)" },
    };

    [Theory]
    [MemberData(nameof(SerialFailures))]
    public async Task Serial_port_failures_are_explained_in_the_tab(SerialPortProblem problem, string expectedText)
    {
        var serial = new FakeSerialSessionFactory();
        serial.Sessions.Enqueue(() => throw new SerialPortUnavailableException("COM3", problem));
        var tab = CreateSerialTab(serial, out var output);

        await tab.ConnectAsync(80, 24);

        Assert.Equal(TerminalStatus.Failed, tab.Status);
        Assert.Equal(expectedText, tab.StatusText);
        Assert.Contains(expectedText, output.ToString());
        Assert.True(tab.CanReconnect);
    }

    [Fact]
    public async Task An_unplugged_device_is_reported_and_the_port_can_be_reopened()
    {
        var serial = new FakeSerialSessionFactory();
        var first = new FakeTerminalSession { CanSendBreak = true };
        var second = new FakeTerminalSession { CanSendBreak = true };
        serial.Sessions.Enqueue(() => first);
        serial.Sessions.Enqueue(() => second);
        var tab = CreateSerialTab(serial, out _);
        await tab.ConnectAsync(80, 24);

        first.Close(new IOException("Le périphérique ne reconnaît pas la commande."));

        Assert.Equal(TerminalStatus.Disconnected, tab.Status);
        Assert.Equal("Terminal_SerialLost(COM3)", tab.StatusText);
        Assert.True(first.Disposed);

        await tab.ReconnectCommand.ExecuteAsync(null);
        Assert.True(tab.IsConnected);
        Assert.Equal(2, serial.Requests.Count);
    }

    [Fact]
    public async Task Break_is_sent_only_while_a_capable_session_is_connected()
    {
        var serial = new FakeSerialSessionFactory();
        var session = new FakeTerminalSession { CanSendBreak = true };
        serial.Sessions.Enqueue(() => session);
        var tab = CreateSerialTab(serial, out _);
        Assert.False(tab.SendBreakCommand.CanExecute(null));

        await tab.ConnectAsync(80, 24);
        Assert.True(tab.CanSendBreak);
        tab.SendBreakCommand.Execute(null);

        session.Close();
        Assert.False(tab.CanSendBreak);
        Assert.False(tab.SendBreakCommand.CanExecute(null));
        tab.SendBreak();

        Assert.Equal(1, session.Breaks);
    }

    [Fact]
    public async Task Break_is_not_offered_on_an_ssh_session()
    {
        _factory.Terminals.Enqueue(() => new FakeTerminalSession());
        var tab = CreateTab(out _);

        await tab.ConnectAsync(80, 24);

        Assert.False(tab.IsSerial);
        Assert.False(tab.CanSendBreak);
        Assert.False(tab.SendBreakCommand.CanExecute(null));
    }
}
