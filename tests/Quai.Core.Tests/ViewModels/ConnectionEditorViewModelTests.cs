using Quai.Core.Models;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.ViewModels;

public class ConnectionEditorViewModelTests
{
    private static ConnectionEditorViewModel NewEditor() => new(new KeyStrings())
    {
        Host = "example.org",
        Username = "alice",
    };

    [Fact]
    public void A_new_editor_has_sensible_defaults()
    {
        var editor = new ConnectionEditorViewModel(new KeyStrings());

        Assert.True(editor.IsNew);
        Assert.Equal(22, editor.Port);
        Assert.True(editor.IsPasswordAuthentication);
        Assert.True(editor.SavePassword);
        Assert.Equal("Editor_TitleNew", editor.Title);
    }

    [Fact]
    public void Valid_input_passes_validation()
    {
        var editor = NewEditor();

        Assert.True(editor.Validate());
        Assert.False(editor.HasError);
    }

    [Theory]
    [InlineData("", "alice", 22.0, "Editor_ErrorHostRequired")]
    [InlineData("bad host", "alice", 22.0, "Editor_ErrorHostInvalid")]
    [InlineData("example.org", " ", 22.0, "Editor_ErrorUserRequired")]
    [InlineData("example.org", "alice", 0.0, "Editor_ErrorPortRange")]
    [InlineData("example.org", "alice", 70000.0, "Editor_ErrorPortRange")]
    [InlineData("example.org", "alice", 22.5, "Editor_ErrorPortRange")]
    [InlineData("example.org", "alice", double.NaN, "Editor_ErrorPortRange")]
    public void Invalid_input_is_reported(string host, string user, double port, string expectedError)
    {
        var editor = NewEditor();
        editor.Host = host;
        editor.Username = user;
        editor.Port = port;

        Assert.False(editor.Validate());
        Assert.Contains(expectedError, editor.ErrorMessage);
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("::1")]
    [InlineData("srv-01.intra.example")]
    public void Ip_addresses_and_host_names_are_accepted(string host)
    {
        var editor = NewEditor();
        editor.Host = host;

        Assert.True(editor.Validate());
    }

    [Fact]
    public void Private_key_authentication_requires_a_key_file()
    {
        var editor = NewEditor();
        editor.AuthenticationIndex = (int)AuthenticationKind.PrivateKey;

        Assert.False(editor.Validate());
        Assert.Contains("Editor_ErrorKeyRequired", editor.ErrorMessage);

        editor.PrivateKeyPath = "C:\\keys\\id_ed25519";
        Assert.True(editor.Validate());
    }

    [Fact]
    public void BuildProfile_maps_every_field_and_keeps_the_identity_when_editing()
    {
        var existing = new ConnectionProfile { Name = "Ancien", Host = "old.example.org", Username = "bob" };
        var editor = new ConnectionEditorViewModel(new KeyStrings(), existing)
        {
            Name = " Nouveau ",
            Host = "new.example.org",
            Port = 2200,
            Username = "carol",
            AuthenticationIndex = (int)AuthenticationKind.PrivateKey,
            PrivateKeyPath = "C:\\keys\\id_rsa",
            Folder = "Prod\\Web",
            TagsText = "web, prod",
            Notes = "note",
            Password = "ne-doit-pas-sortir",
            Passphrase = "non-plus",
        };

        var profile = editor.BuildProfile();

        Assert.False(editor.IsNew);
        Assert.Equal(existing.Id, profile.Id);
        Assert.Equal("Nouveau", profile.Name);
        Assert.Equal("new.example.org", profile.Host);
        Assert.Equal(2200, profile.Port);
        Assert.Equal("carol", profile.Username);
        Assert.Equal(AuthenticationKind.PrivateKey, profile.Authentication);
        Assert.Equal("C:\\keys\\id_rsa", profile.PrivateKeyPath);
        Assert.Equal("Prod/Web", profile.Folder);
        Assert.Equal(["web", "prod"], profile.Tags);
        Assert.Equal("note", profile.Notes);
    }

    [Fact]
    public void The_key_path_is_dropped_for_password_authentication()
    {
        var editor = NewEditor();
        editor.PrivateKeyPath = "C:\\keys\\id_rsa";
        editor.AuthenticationIndex = (int)AuthenticationKind.Password;

        Assert.Null(editor.BuildProfile().PrivateKeyPath);
    }

    [Fact]
    public void Stored_secrets_change_the_placeholders()
    {
        var existing = new ConnectionProfile { Host = "h", Username = "u" };
        var editor = new ConnectionEditorViewModel(new KeyStrings(), existing, hasStoredPassword: true);

        Assert.Equal("Editor_SecretUnchanged", editor.PasswordPlaceholder);
        Assert.Equal("Editor_PassphrasePlaceholder", editor.PassphrasePlaceholder);
        Assert.True(editor.SavePassword);
    }

    private static ConnectionEditorViewModel NewSerialEditor(FakeSerialPortCatalog? ports = null)
    {
        var editor = new ConnectionEditorViewModel(new KeyStrings(), serialPorts: ports);
        editor.ProtocolIndex = (int)ConnectionProtocol.Serial;
        return editor;
    }

    [Fact]
    public void A_new_connection_is_ssh_and_does_not_read_the_serial_ports()
    {
        var ports = new FakeSerialPortCatalog { Ports = { "COM3" } };
        var editor = new ConnectionEditorViewModel(new KeyStrings(), serialPorts: ports);

        Assert.True(editor.IsSsh);
        Assert.False(editor.IsSerial);
        Assert.Equal(0, ports.Reads);
        Assert.Equal("9600", editor.BaudRateText);
        Assert.Contains("115200", editor.BaudRates);
    }

    [Fact]
    public void Switching_to_serial_lists_the_ports_and_proposes_the_first_one()
    {
        var ports = new FakeSerialPortCatalog { Ports = { "COM3", "COM7" } };

        var editor = NewSerialEditor(ports);

        Assert.True(editor.IsSerial);
        Assert.False(editor.IsSsh);
        Assert.Equal(["COM3", "COM7"], editor.SerialPorts);
        Assert.Equal("COM3", editor.SerialPortName);
        Assert.False(editor.NoSerialPortDetected);
    }

    [Fact]
    public void Refreshing_the_ports_keeps_the_current_choice()
    {
        var ports = new FakeSerialPortCatalog { Ports = { "COM3", "COM7" } };
        var editor = NewSerialEditor(ports);
        editor.SerialPortName = "COM7";

        ports.Ports.Clear();
        ports.Ports.AddRange(["COM1", "COM7", "COM12"]);
        editor.RefreshSerialPortsCommand.Execute(null);

        Assert.Equal(["COM1", "COM7", "COM12"], editor.SerialPorts);
        Assert.Equal("COM7", editor.SerialPortName);
    }

    [Fact]
    public void No_detected_port_is_reported_and_the_name_stays_typeable()
    {
        var editor = NewSerialEditor(new FakeSerialPortCatalog());
        editor.SerialPortName = "com4";

        Assert.True(editor.NoSerialPortDetected);
        Assert.True(editor.Validate());
        Assert.Equal("COM4", editor.BuildProfile().Serial!.PortName);
    }

    [Theory]
    [InlineData("", "9600", "Editor_ErrorSerialPortRequired")]
    [InlineData("LPT1", "9600", "Editor_ErrorSerialPortInvalid")]
    [InlineData("COM3", "", "Editor_ErrorBaudRate")]
    [InlineData("COM3", "rapide", "Editor_ErrorBaudRate")]
    [InlineData("COM3", "0", "Editor_ErrorBaudRate")]
    [InlineData("COM3", "-9600", "Editor_ErrorBaudRate")]
    [InlineData("COM3", "9600.5", "Editor_ErrorBaudRate")]
    [InlineData("COM3", "99000000", "Editor_ErrorBaudRate")]
    public void Invalid_serial_input_is_reported(string port, string baud, string expectedError)
    {
        var editor = NewSerialEditor();
        editor.SerialPortName = port;
        editor.BaudRateText = baud;

        Assert.False(editor.Validate());
        Assert.Contains(expectedError, editor.ErrorMessage);
    }

    [Fact]
    public void Ssh_fields_are_not_required_for_a_serial_connection()
    {
        var editor = NewSerialEditor();
        editor.SerialPortName = "COM3";
        editor.Host = string.Empty;
        editor.Username = string.Empty;

        Assert.True(editor.Validate());
        Assert.False(editor.HasError);
    }

    [Fact]
    public void A_serial_connection_is_built_with_its_link_settings_and_no_ssh_field()
    {
        var editor = NewSerialEditor();
        editor.Name = "Console";
        editor.Host = "reste.example.org";
        editor.Username = "alice";
        editor.Password = "ne-doit-pas-sortir";
        editor.SerialPortName = " com5 ";
        editor.BaudRateText = "115 200";
        editor.DataBitsIndex = 2;
        editor.ParityIndex = (int)SerialParity.Even;
        editor.StopBitsIndex = (int)SerialStopBits.Two;
        editor.FlowControlIndex = (int)SerialFlowControl.XonXoff;

        Assert.True(editor.Validate());
        var profile = editor.BuildProfile();

        Assert.Equal(ConnectionProtocol.Serial, profile.Protocol);
        Assert.Equal("Console", profile.Name);
        Assert.Equal("COM5 · 115200 7E2 · XON/XOFF", profile.Endpoint);
        Assert.Equal(string.Empty, profile.Host);
        Assert.Equal(string.Empty, profile.Username);
    }

    [Fact]
    public void Editing_a_serial_connection_restores_its_settings()
    {
        var existing = new ConnectionProfile
        {
            Name = "Routeur",
            Protocol = ConnectionProtocol.Serial,
            Serial = new SerialSettings
            {
                PortName = "COM9",
                BaudRate = 57600,
                DataBits = 7,
                Parity = SerialParity.Odd,
                StopBits = SerialStopBits.OnePointFive,
                FlowControl = SerialFlowControl.RtsCts,
            },
        };
        var ports = new FakeSerialPortCatalog { Ports = { "COM3" } };

        var editor = new ConnectionEditorViewModel(new KeyStrings(), existing, serialPorts: ports);

        Assert.True(editor.IsSerial);
        Assert.Equal("COM9", editor.SerialPortName);
        Assert.Equal(["COM3"], editor.SerialPorts);
        Assert.Equal("57600", editor.BaudRateText);
        Assert.Equal(2, editor.DataBitsIndex);
        Assert.Equal((int)SerialParity.Odd, editor.ParityIndex);
        Assert.Equal((int)SerialStopBits.OnePointFive, editor.StopBitsIndex);
        Assert.Equal((int)SerialFlowControl.RtsCts, editor.FlowControlIndex);
        Assert.Equal(existing.Endpoint, editor.BuildProfile().Endpoint);
    }

    [Fact]
    public void Switching_back_to_ssh_drops_the_serial_settings()
    {
        var editor = NewSerialEditor();
        editor.SerialPortName = "COM3";
        editor.ProtocolIndex = (int)ConnectionProtocol.Ssh;
        editor.Host = "example.org";
        editor.Username = "alice";

        Assert.True(editor.Validate());
        var profile = editor.BuildProfile();

        Assert.Equal(ConnectionProtocol.Ssh, profile.Protocol);
        Assert.Null(profile.Serial);
        Assert.Equal("alice@example.org", profile.Endpoint);
    }
}
