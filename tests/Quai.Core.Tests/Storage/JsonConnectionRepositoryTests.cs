using Quai.Core.Models;
using Quai.Core.Storage;
using Quai.Core.Tests.Support;

namespace Quai.Core.Tests.Storage;

public class JsonConnectionRepositoryTests
{
    private static ConnectionProfile Sample(string name = "Serveur web") => new()
    {
        Name = name,
        Host = "web.example.org",
        Port = 2222,
        Username = "deploy",
        Authentication = AuthenticationKind.PrivateKey,
        PrivateKeyPath = "C:\\keys\\id_ed25519",
        Folder = "Prod/Web",
        Tags = ["web", "prod"],
        Notes = "Frontal",
    };

    [Fact]
    public void Empty_when_the_file_does_not_exist()
    {
        using var temp = new TempDirectory();
        var repository = new JsonConnectionRepository(temp.File("connections.json"));

        Assert.Empty(repository.GetAll());
    }

    [Fact]
    public void Saved_connections_survive_a_reload()
    {
        using var temp = new TempDirectory();
        var path = temp.File("connections.json");
        var profile = Sample();

        new JsonConnectionRepository(path).Save(profile);
        var reloaded = new JsonConnectionRepository(path).GetAll();

        var loaded = Assert.Single(reloaded);
        Assert.Equal(profile.Id, loaded.Id);
        Assert.Equal("Serveur web", loaded.Name);
        Assert.Equal("web.example.org", loaded.Host);
        Assert.Equal(2222, loaded.Port);
        Assert.Equal("deploy", loaded.Username);
        Assert.Equal(AuthenticationKind.PrivateKey, loaded.Authentication);
        Assert.Equal("C:\\keys\\id_ed25519", loaded.PrivateKeyPath);
        Assert.Equal("Prod/Web", loaded.Folder);
        Assert.Equal(["web", "prod"], loaded.Tags);
        Assert.Equal("Frontal", loaded.Notes);
    }

    [Fact]
    public void Save_replaces_the_connection_with_the_same_id()
    {
        using var temp = new TempDirectory();
        var repository = new JsonConnectionRepository(temp.File("connections.json"));
        var profile = Sample();
        repository.Save(profile);

        profile.Name = "Renommé";
        repository.Save(profile);

        Assert.Equal("Renommé", Assert.Single(repository.GetAll()).Name);
    }

    [Fact]
    public void Delete_removes_and_persists()
    {
        using var temp = new TempDirectory();
        var path = temp.File("connections.json");
        var repository = new JsonConnectionRepository(path);
        var first = Sample("A");
        var second = Sample("B");
        repository.Save(first);
        repository.Save(second);

        Assert.True(repository.Delete(first.Id));
        Assert.False(repository.Delete(first.Id));

        Assert.Equal("B", Assert.Single(new JsonConnectionRepository(path).GetAll()).Name);
    }

    [Fact]
    public void GetAll_is_sorted_by_name_and_returns_copies()
    {
        using var temp = new TempDirectory();
        var repository = new JsonConnectionRepository(temp.File("connections.json"));
        repository.Save(Sample("zeta"));
        repository.Save(Sample("Alpha"));

        var all = repository.GetAll();
        all[0].Name = "modifié hors du dépôt";

        Assert.Equal(["Alpha", "zeta"], repository.GetAll().Select(p => p.Name));
    }

    [Fact]
    public void Changed_is_raised_on_save_and_delete()
    {
        using var temp = new TempDirectory();
        var repository = new JsonConnectionRepository(temp.File("connections.json"));
        var count = 0;
        repository.Changed += (_, _) => count++;
        var profile = Sample();

        repository.Save(profile);
        repository.Delete(profile.Id);

        Assert.Equal(2, count);
    }

    [Fact]
    public void A_corrupt_file_is_set_aside_instead_of_being_overwritten()
    {
        using var temp = new TempDirectory();
        var path = temp.File("connections.json");
        File.WriteAllText(path, "{ ceci n'est pas du JSON");

        var repository = new JsonConnectionRepository(path);

        Assert.Empty(repository.GetAll());
        Assert.Single(Directory.GetFiles(temp.Path, "connections.json.corrompu-*"));
    }

    [Fact]
    public void The_file_is_readable_json_without_any_secret_field()
    {
        using var temp = new TempDirectory();
        var path = temp.File("connections.json");
        new JsonConnectionRepository(path).Save(Sample());

        var json = File.ReadAllText(path);

        Assert.Contains("\"host\": \"web.example.org\"", json);
        Assert.Contains("\"authentication\": \"privateKey\"", json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passphrase", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    [Fact]
    public void A_serial_connection_survives_a_reload()
    {
        using var temp = new TempDirectory();
        var path = temp.File("connections.json");
        var profile = new ConnectionProfile
        {
            Name = "Console",
            Protocol = ConnectionProtocol.Serial,
            Serial = new SerialSettings
            {
                PortName = "COM3",
                BaudRate = 115200,
                DataBits = 7,
                Parity = SerialParity.Even,
                StopBits = SerialStopBits.Two,
                FlowControl = SerialFlowControl.RtsCts,
            },
        };

        new JsonConnectionRepository(path).Save(profile);
        var loaded = Assert.Single(new JsonConnectionRepository(path).GetAll());
        var json = File.ReadAllText(path);

        Assert.Equal(ConnectionProtocol.Serial, loaded.Protocol);
        Assert.Equal("COM3 · 115200 7E2 · RTS/CTS", loaded.Endpoint);
        Assert.Contains("\"protocol\": \"serial\"", json);
        Assert.Contains("\"portName\": \"COM3\"", json);
        Assert.Contains("\"flowControl\": \"rtsCts\"", json);
    }

    [Fact]
    public void A_book_saved_before_serial_support_loads_as_ssh()
    {
        using var temp = new TempDirectory();
        var path = temp.File("connections.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "connections": [
                { "id": "6f0d1c3e-8a52-4a0e-9a35-1d2b3c4d5e6f", "name": "Ancien", "host": "old.example.org", "port": 22, "username": "root" }
              ]
            }
            """);

        var loaded = Assert.Single(new JsonConnectionRepository(path).GetAll());

        Assert.Equal(ConnectionProtocol.Ssh, loaded.Protocol);
        Assert.False(loaded.IsSerial);
        Assert.Null(loaded.Serial);
        Assert.Equal("root@old.example.org", loaded.Endpoint);
    }
}
