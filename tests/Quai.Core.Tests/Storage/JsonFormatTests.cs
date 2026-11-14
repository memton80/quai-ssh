using System.Text.Json;
using System.Text.Json.Serialization;
using Quai.Core.Models;
using Quai.Core.Storage;

namespace Quai.Core.Tests.Storage;

/// <summary>
/// Le sérialiseur généré à la compilation (<see cref="QuaiJsonContext"/>) doit écrire et lire exactement comme
/// l'ancien sérialiseur par réflexion : les fichiers des versions précédentes restent lisibles, et inversement.
/// </summary>
public class JsonFormatTests
{
    // Options de l'ancien sérialiseur par réflexion (jusqu'à la 1.0.1).
    private static readonly JsonSerializerOptions Reflection = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private static ConnectionDocument SampleDocument() => new()
    {
        Connections =
        [
            new ConnectionProfile
            {
                Id = Guid.Parse("6f0d1c3e-8a52-4a0e-9a35-1d2b3c4d5e6f"),
                Name = "Frontal",
                Host = "web.example.org",
                Port = 2222,
                Username = "deploy",
                Authentication = AuthenticationKind.PrivateKey,
                PrivateKeyPath = @"C:\Users\moi\.ssh\id_ed25519",
                Folder = "Production/Web",
                Tags = ["web", "prod"],
                Notes = "Accents : é à ç « » \"guillemets\"",
                CreatedAt = new DateTimeOffset(2026, 11, 14, 10, 30, 0, TimeSpan.FromHours(1)),
                LastConnectedAt = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero),
            },
            new ConnectionProfile
            {
                Id = Guid.Parse("0b6b2d0e-1f6f-4c55-9f0a-5d8f1f2c3b4a"),
                Name = "Console",
                Protocol = ConnectionProtocol.Serial,
                Serial = new SerialSettings
                {
                    PortName = "COM3",
                    BaudRate = 115200,
                    DataBits = 7,
                    Parity = SerialParity.Even,
                    StopBits = SerialStopBits.OnePointFive,
                    FlowControl = SerialFlowControl.RtsCts,
                },
                CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            },
        ],
    };

    [Fact]
    public void Connections_are_written_exactly_as_before()
    {
        var document = SampleDocument();

        var generated = JsonSerializer.Serialize(document, QuaiJsonContext.Default.ConnectionDocument);
        var reflection = JsonSerializer.Serialize(document, Reflection);

        Assert.Equal(reflection, generated);
        Assert.Contains("\"authentication\": \"privateKey\"", generated);
        Assert.Contains("\"stopBits\": \"onePointFive\"", generated);
    }

    [Fact]
    public void Settings_are_written_exactly_as_before()
    {
        var settings = new AppSettings { Theme = AppTheme.Dark, TerminalFontFamily = "Consolas", TerminalFontSize = 14, CheckForUpdates = false };

        Assert.Equal(
            JsonSerializer.Serialize(settings, Reflection),
            JsonSerializer.Serialize(settings, QuaiJsonContext.Default.AppSettings));
    }

    [Theory]
    [InlineData("""{ "theme": "dark", "terminalFontSize": 14, }""")]
    [InlineData("""{ "theme": "Dark" /* commentaire */ }""")]
    [InlineData("""{ "theme": 2, "checkForUpdates": false }""")]
    [InlineData("""{ "Theme": "light", "inconnu": [1, 2] }""")]
    public void Settings_are_read_exactly_as_before(string json)
    {
        var generated = JsonSerializer.Deserialize(json, QuaiJsonContext.Default.AppSettings);
        var reflection = JsonSerializer.Deserialize<AppSettings>(json, Reflection);

        Assert.Equal(JsonSerializer.Serialize(reflection, Reflection), JsonSerializer.Serialize(generated, Reflection));
    }

    [Fact]
    public void Connections_written_by_the_old_serializer_are_read_back_identically()
    {
        var json = JsonSerializer.Serialize(SampleDocument(), Reflection);

        var generated = JsonSerializer.Deserialize(json, QuaiJsonContext.Default.ConnectionDocument);

        Assert.Equal(json, JsonSerializer.Serialize(generated, Reflection));
    }
}
