using Quai.Core.Models;

namespace Quai.Core.Tests.Models;

public class SerialSettingsTests
{
    private static ConnectionProfile SerialProfile(string port = "COM3", int baud = 115200) => new()
    {
        Protocol = ConnectionProtocol.Serial,
        Serial = new SerialSettings { PortName = port, BaudRate = baud },
    };

    [Fact]
    public void Defaults_are_those_of_putty()
    {
        var settings = new SerialSettings();

        Assert.Equal(9600, settings.BaudRate);
        Assert.Equal("8N1", settings.Frame);
        Assert.Equal(SerialFlowControl.None, settings.FlowControl);
    }

    [Theory]
    [InlineData(8, SerialParity.None, SerialStopBits.One, "8N1")]
    [InlineData(7, SerialParity.Even, SerialStopBits.One, "7E1")]
    [InlineData(7, SerialParity.Odd, SerialStopBits.Two, "7O2")]
    [InlineData(5, SerialParity.Mark, SerialStopBits.OnePointFive, "5M1.5")]
    [InlineData(8, SerialParity.Space, SerialStopBits.One, "8S1")]
    public void Frame_uses_the_usual_notation(int dataBits, SerialParity parity, SerialStopBits stopBits, string expected) =>
        Assert.Equal(expected, new SerialSettings { DataBits = dataBits, Parity = parity, StopBits = stopBits }.Frame);

    [Theory]
    [InlineData(SerialFlowControl.None, "COM3 · 115200 8N1")]
    [InlineData(SerialFlowControl.XonXoff, "COM3 · 115200 8N1 · XON/XOFF")]
    [InlineData(SerialFlowControl.RtsCts, "COM3 · 115200 8N1 · RTS/CTS")]
    public void Summary_shows_port_speed_frame_and_flow_control(SerialFlowControl flow, string expected) =>
        Assert.Equal(expected, new SerialSettings { PortName = "COM3", BaudRate = 115200, FlowControl = flow }.Summary);

    [Fact]
    public void Normalize_restores_defaults_for_out_of_range_values()
    {
        var settings = new SerialSettings
        {
            PortName = " com7 ",
            BaudRate = 0,
            DataBits = 9,
            Parity = (SerialParity)42,
            StopBits = (SerialStopBits)42,
            FlowControl = (SerialFlowControl)42,
        };

        settings.Normalize();

        Assert.Equal("COM7", settings.PortName);
        Assert.Equal(SerialSettings.DefaultBaudRate, settings.BaudRate);
        Assert.Equal(SerialSettings.DefaultDataBits, settings.DataBits);
        Assert.Equal("8N1", settings.Frame);
        Assert.Equal(SerialFlowControl.None, settings.FlowControl);
    }

    [Theory]
    [InlineData("COM1", true)]
    [InlineData("com12", true)]
    [InlineData(" COM 3 ", true)]
    [InlineData("COM0", false)]
    [InlineData("COM", false)]
    [InlineData("COM-1", false)]
    [InlineData("COM3a", false)]
    [InlineData("LPT1", false)]
    [InlineData(@"\\.\COM10", false)]
    [InlineData("/dev/ttyUSB0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_com_port_names_are_valid(string? name, bool expected) =>
        Assert.Equal(expected, SerialPortName.IsValid(name));

    [Fact]
    public void Detected_ports_are_cleaned_deduplicated_and_sorted_by_number() =>
        Assert.Equal(
            ["COM1", "COM2", "COM10"],
            SerialPortName.Sort(["COM10", "com2", "COM1\0", "COM2", "LPT1", "", null, "/dev/ttyS0"]));

    [Fact]
    public void A_serial_profile_shows_its_link_and_is_named_after_its_port()
    {
        var profile = SerialProfile();

        Assert.True(profile.IsSerial);
        Assert.Equal("COM3 · 115200 8N1", profile.Endpoint);
        Assert.Equal("COM3", profile.DisplayName);
    }

    [Fact]
    public void Normalize_keeps_no_ssh_field_on_a_serial_profile()
    {
        var profile = SerialProfile(port: "com4");
        profile.Host = "ancien.example.org";
        profile.Port = 2222;
        profile.Username = "root";
        profile.Authentication = AuthenticationKind.PrivateKey;
        profile.PrivateKeyPath = "C:\\cle";

        profile.Normalize();

        Assert.Equal("COM4", profile.Name);
        Assert.Equal("COM4", profile.Serial!.PortName);
        Assert.Equal(string.Empty, profile.Host);
        Assert.Equal(ConnectionProfile.DefaultPort, profile.Port);
        Assert.Equal(string.Empty, profile.Username);
        Assert.Equal(AuthenticationKind.Password, profile.Authentication);
        Assert.Null(profile.PrivateKeyPath);
    }

    [Fact]
    public void Normalize_drops_serial_settings_from_an_ssh_profile()
    {
        var profile = new ConnectionProfile { Host = "example.org", Username = "alice", Serial = new SerialSettings { PortName = "COM3" } };

        profile.Normalize();

        Assert.Null(profile.Serial);
        Assert.Equal("alice@example.org", profile.Endpoint);
    }

    [Fact]
    public void A_serial_profile_without_settings_gets_the_defaults()
    {
        var profile = new ConnectionProfile { Protocol = ConnectionProtocol.Serial };

        profile.Normalize();

        Assert.NotNull(profile.Serial);
        Assert.Equal(SerialSettings.DefaultBaudRate, profile.Serial.BaudRate);
    }

    [Fact]
    public void Clone_copies_the_serial_settings()
    {
        var profile = SerialProfile();
        var clone = profile.Clone();
        clone.Serial!.BaudRate = 9600;

        Assert.Equal(ConnectionProtocol.Serial, clone.Protocol);
        Assert.Equal(115200, profile.Serial!.BaudRate);
    }
}
