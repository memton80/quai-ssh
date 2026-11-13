using Quai.Core.Ssh;
using Quai.Core.Tests.Support;

namespace Quai.Core.Tests.Ssh;

public class KnownHostsStoreTests
{
    private static readonly byte[] KeyA = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] KeyB = [9, 9, 9, 9, 9, 9, 9, 9];

    private static HostKeyInfo Key(string host = "example.org", int port = 22, byte[]? key = null, string type = "ssh-ed25519") =>
        new(host, port, type, key ?? KeyA);

    [Fact]
    public void An_unseen_host_is_unknown()
    {
        using var temp = new TempDirectory();
        var store = new KnownHostsStore(temp.File("known_hosts"));

        Assert.Equal(HostKeyStatus.Unknown, store.Check(Key()));
    }

    [Fact]
    public void A_trusted_key_is_recognised_after_a_reload()
    {
        using var temp = new TempDirectory();
        var path = temp.File("known_hosts");
        new KnownHostsStore(path).Trust(Key());

        Assert.Equal(HostKeyStatus.Trusted, new KnownHostsStore(path).Check(Key()));
    }

    [Fact]
    public void A_different_key_for_a_known_host_is_reported_as_changed()
    {
        using var temp = new TempDirectory();
        var store = new KnownHostsStore(temp.File("known_hosts"));
        store.Trust(Key());

        Assert.Equal(HostKeyStatus.Changed, store.Check(Key(key: KeyB)));
        Assert.Equal(HostKeyStatus.Changed, store.Check(Key(type: "ssh-rsa")));
    }

    [Fact]
    public void Hosts_are_distinguished_by_name_and_port()
    {
        using var temp = new TempDirectory();
        var store = new KnownHostsStore(temp.File("known_hosts"));
        store.Trust(Key("example.org", 22));

        Assert.Equal(HostKeyStatus.Unknown, store.Check(Key("example.org", 2222)));
        Assert.Equal(HostKeyStatus.Unknown, store.Check(Key("other.example.org", 22)));
        Assert.Equal(HostKeyStatus.Trusted, store.Check(Key("EXAMPLE.org", 22)));
    }

    [Fact]
    public void Trust_replaces_the_previous_key_of_the_host_only()
    {
        using var temp = new TempDirectory();
        var store = new KnownHostsStore(temp.File("known_hosts"));
        store.Trust(Key("a.example.org"));
        store.Trust(Key("b.example.org"));

        store.Trust(Key("a.example.org", key: KeyB));

        Assert.Equal(KeyB, Assert.Single(store.Find("a.example.org", 22)).Key);
        Assert.Equal(HostKeyStatus.Trusted, store.Check(Key("b.example.org")));
    }

    [Fact]
    public void Forget_removes_the_host()
    {
        using var temp = new TempDirectory();
        var store = new KnownHostsStore(temp.File("known_hosts"));
        store.Trust(Key());

        Assert.True(store.Forget("example.org", 22));
        Assert.False(store.Forget("example.org", 22));
        Assert.Equal(HostKeyStatus.Unknown, store.Check(Key()));
    }

    [Fact]
    public void The_file_uses_the_openssh_format()
    {
        using var temp = new TempDirectory();
        var path = temp.File("known_hosts");
        var store = new KnownHostsStore(path);
        store.Trust(Key("example.org", 22));
        store.Trust(Key("example.org", 2222, KeyB));

        var lines = File.ReadAllLines(path).Where(l => !l.StartsWith('#')).ToArray();

        Assert.Equal(
            [
                $"example.org ssh-ed25519 {Convert.ToBase64String(KeyA)}",
                $"[example.org]:2222 ssh-ed25519 {Convert.ToBase64String(KeyB)}",
            ],
            lines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# commentaire")]
    [InlineData("|1|hache|hache ssh-ed25519 AQID")]
    [InlineData("@revoked example.org ssh-ed25519 AQID")]
    [InlineData("example.org ssh-ed25519")]
    [InlineData("example.org ssh-ed25519 pas-du-base64!")]
    [InlineData("[example.org]:abc ssh-ed25519 AQID")]
    public void Unusable_lines_are_ignored(string line) =>
        Assert.Empty(KnownHostsStore.ParseLine(line));

    [Fact]
    public void A_line_may_list_several_hosts()
    {
        var entries = KnownHostsStore.ParseLine("Example.org,[10.0.0.1]:2200 ssh-rsa AQID commentaire").ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal(("example.org", 22), (entries[0].Host, entries[0].Port));
        Assert.Equal(("10.0.0.1", 2200), (entries[1].Host, entries[1].Port));
        Assert.Equal([1, 2, 3], entries[1].Key);
    }

    [Fact]
    public void Fingerprint_matches_the_openssh_sha256_format()
    {
        // SHA-256 de 3 octets 01 02 03, en base64 sans remplissage.
        var info = new HostKeyInfo("h", 22, "ssh-ed25519", [1, 2, 3]);

        Assert.Equal("SHA256:A5BYxvLAy0ksUzsKTRTvd8wPeKvMztUofYShogEc+4E", info.Fingerprint);
        Assert.Equal("h", info.Endpoint);
        Assert.Equal("h:2222", (info with { Port = 2222 }).Endpoint);
    }
}
