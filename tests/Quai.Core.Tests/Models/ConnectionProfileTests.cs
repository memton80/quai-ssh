using Quai.Core.Models;

namespace Quai.Core.Tests.Models;

public class ConnectionProfileTests
{
    [Theory]
    [InlineData("root", "example.org", 22, "root@example.org")]
    [InlineData("root", "example.org", 2222, "root@example.org:2222")]
    [InlineData("", "example.org", 22, "example.org")]
    public void Endpoint_shows_port_only_when_not_default(string user, string host, int port, string expected)
    {
        var profile = new ConnectionProfile { Username = user, Host = host, Port = port };
        Assert.Equal(expected, profile.Endpoint);
    }

    [Fact]
    public void Normalize_trims_and_falls_back_to_endpoint_for_the_name()
    {
        var profile = new ConnectionProfile
        {
            Name = "  ",
            Host = " example.org ",
            Username = " alice ",
            Folder = "\\Prod//Web/ ",
            Tags = [" web ", "WEB", "", "#db"],
            PrivateKeyPath = " \"C:\\keys\\id_ed25519\" ",
        };

        profile.Normalize();

        Assert.Equal("alice@example.org", profile.Name);
        Assert.Equal("example.org", profile.Host);
        Assert.Equal("Prod/Web", profile.Folder);
        Assert.Equal(["web", "db"], profile.Tags);
        Assert.Equal("C:\\keys\\id_ed25519", profile.PrivateKeyPath);
    }

    [Fact]
    public void Clone_is_deep_for_tags()
    {
        var profile = new ConnectionProfile { Tags = ["a"] };
        var clone = profile.Clone();
        clone.Tags.Add("b");

        Assert.Equal(profile.Id, clone.Id);
        Assert.Single(profile.Tags);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData("a", "a")]
    [InlineData("/a//b/", "a/b")]
    [InlineData("a\\b \\ c", "a/b/c")]
    public void FolderPath_normalizes(string? input, string expected) =>
        Assert.Equal(expected, FolderPath.Normalize(input));

    [Fact]
    public void FolderPath_lists_ancestors() =>
        Assert.Equal(["a", "a/b", "a/b/c"], FolderPath.SelfAndAncestors("a/b/c"));

    [Theory]
    [InlineData("Prod/Web", "Prod", true)]
    [InlineData("Prod", "Prod", true)]
    [InlineData("prod/web", "PROD", true)]
    [InlineData("Production", "Prod", false)]
    [InlineData("", "Prod", false)]
    [InlineData("Prod", "", true)]
    public void FolderPath_detects_descendants(string folder, string ancestor, bool expected) =>
        Assert.Equal(expected, FolderPath.IsSameOrDescendant(folder, ancestor));

    [Fact]
    public void TagList_parses_and_deduplicates() =>
        Assert.Equal(["prod", "web", "db"], TagList.Parse("prod, web; PROD ,, #db"));
}
