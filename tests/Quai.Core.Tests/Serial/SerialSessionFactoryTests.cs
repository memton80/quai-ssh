using Quai.Core.Models;
using Quai.Core.Serial;
using Quai.Core.Terminal;
using Quai.Core.Tests.Support;

namespace Quai.Core.Tests.Serial;

public class SerialSessionFactoryTests
{
    public static TheoryData<Exception, SerialPortProblem> OpeningErrors => new()
    {
        { new FileNotFoundException("Could not find file 'COM9'."), SerialPortProblem.NotFound },
        { new DirectoryNotFoundException(), SerialPortProblem.NotFound },
        { new UnauthorizedAccessException("Access to the path 'COM3' is denied."), SerialPortProblem.InUse },
        { new IOException("The process cannot access the file.", unchecked((int)0x80070020)), SerialPortProblem.InUse },
        { new IOException("The parameter is incorrect.", unchecked((int)0x80070057)), SerialPortProblem.SettingsRejected },
        { new ArgumentOutOfRangeException("baudRate", "The maximum baud rate for the device is 115200."), SerialPortProblem.SettingsRejected },
    };

    [Theory]
    [MemberData(nameof(OpeningErrors))]
    public void Opening_errors_are_translated_into_a_cause(Exception error, SerialPortProblem expected)
    {
        var translated = Assert.IsType<SerialPortUnavailableException>(SerialSessionFactory.Translate("COM3", error));

        Assert.Equal(expected, translated.Problem);
        Assert.Equal("COM3", translated.PortName);
        Assert.Same(error, translated.InnerException);
    }

    [Fact]
    public void Other_errors_are_left_as_they_are()
    {
        var error = new IOException("Erreur matérielle.");

        Assert.Same(error, SerialSessionFactory.Translate("COM3", error));
    }

    /// <summary>Vrai port Windows (sous Unix, System.IO.Ports signale toute erreur d'ouverture comme un refus d'accès).</summary>
    [Fact]
    public async Task A_missing_port_is_reported_as_not_found()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Noms de ports COM : Windows uniquement.");
        var factory = new SerialSessionFactory();
        const string name = "COM249";

        var error = await Assert.ThrowsAsync<SerialPortUnavailableException>(
            () => factory.OpenAsync(new SerialSettings { PortName = name }, TestContext.Current.CancellationToken));

        Assert.Equal(SerialPortProblem.NotFound, error.Problem);
        Assert.Equal(name, error.PortName);
    }

    [Fact]
    public async Task The_terminal_factory_routes_by_connection_type()
    {
        var ssh = new FakeSessionFactory();
        var serial = new FakeSerialSessionFactory();
        var sshSession = new FakeTerminalSession();
        var serialSession = new FakeTerminalSession();
        ssh.Terminals.Enqueue(() => sshSession);
        serial.Sessions.Enqueue(() => serialSession);
        var factory = new TerminalSessionFactory(ssh, serial);
        var ct = TestContext.Current.CancellationToken;

        var openedSsh = await factory.OpenTerminalAsync(new ConnectionProfile { Host = "example.org" }, 100, 30, ct);
        var openedSerial = await factory.OpenTerminalAsync(
            new ConnectionProfile { Protocol = ConnectionProtocol.Serial, Serial = new SerialSettings { PortName = "COM3", BaudRate = 115200 } },
            100,
            30,
            ct);

        Assert.Same(sshSession, openedSsh);
        Assert.Same(serialSession, openedSerial);
        Assert.Equal((100, 30), (Assert.Single(ssh.TerminalRequests).Columns, ssh.TerminalRequests[0].Rows));
        Assert.Equal("COM3 · 115200 8N1", Assert.Single(serial.Requests).Summary);
    }
}
