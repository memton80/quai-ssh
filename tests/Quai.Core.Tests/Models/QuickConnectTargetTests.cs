using Quai.Core.Models;

namespace Quai.Core.Tests.Models;

public class QuickConnectTargetTests
{
    [Theory]
    [InlineData("serveur.example.org", null, "serveur.example.org", 22)]
    [InlineData("alice@serveur", "alice", "serveur", 22)]
    [InlineData("alice@192.168.1.20:2222", "alice", "192.168.1.20", 2222)]
    [InlineData("  alice@serveur:22  ", "alice", "serveur", 22)]
    [InlineData("[::1]:2200", null, "::1", 2200)]
    [InlineData("bob@[fe80::1%eth0]", "bob", "fe80::1%eth0", 22)]
    [InlineData("fe80::1", null, "fe80::1", 22)]
    [InlineData("ssh://alice@serveur:2022/", "alice", "serveur", 2022)]
    [InlineData("ssh alice@serveur", "alice", "serveur", 22)]
    [InlineData("ssh -p 2222 alice@serveur", "alice", "serveur", 2222)]
    [InlineData("ssh alice@serveur -p 2222", "alice", "serveur", 2222)]
    [InlineData("ssh -l bob serveur", "bob", "serveur", 22)]
    [InlineData("ssh -l bob alice@serveur:2022", "bob", "serveur", 2022)]
    [InlineData("jean.dupont@ad.example@bastion", "jean.dupont@ad.example", "bastion", 22)]
    public void Common_forms_are_recognised(string text, string? user, string host, int port)
    {
        Assert.True(QuickConnectTarget.TryParse(text, out var target));
        Assert.Equal(new QuickConnectTarget(user, host, port), target);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("alice@")]
    [InlineData("@serveur")]
    [InlineData("serveur:")]
    [InlineData("serveur:0")]
    [InlineData("serveur:70000")]
    [InlineData("serveur:ssh")]
    [InlineData("[::1")]
    [InlineData("[::1]x")]
    [InlineData("[serveur]:22")]
    [InlineData("ssh")]
    [InlineData("ssh -p")]
    [InlineData("ssh -p abc serveur")]
    [InlineData("ssh -i cle serveur")]
    [InlineData("ssh serveur uptime")]
    [InlineData("serv eur")]
    [InlineData("serveur/chemin")]
    [InlineData("al:ice@serveur")]
    public void Anything_else_is_refused(string? text)
    {
        Assert.False(QuickConnectTarget.TryParse(text, out _));
    }

    [Fact]
    public void The_profile_is_transient_and_named_after_its_address()
    {
        Assert.True(QuickConnectTarget.TryParse("serveur:2222", out var target));

        var profile = target.ToProfile("alice");

        Assert.True(profile.IsTransient);
        Assert.Equal("alice@serveur:2222", profile.Name);
        Assert.Equal(("serveur", 2222, "alice"), (profile.Host, profile.Port, profile.Username));
        Assert.Equal(AuthenticationKind.Password, profile.Authentication);
        Assert.True(profile.Clone().IsTransient);
    }
}
