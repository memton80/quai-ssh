using Quai.Core.Models;
using Quai.Core.Services;

namespace Quai.Core.Tests.Services;

public class ConnectionFilterTests
{
    private static readonly ConnectionProfile[] Profiles =
    [
        new() { Name = "Frontal web", Host = "web01.example.org", Username = "deploy", Folder = "Prod/Web", Tags = ["web", "nginx"] },
        new() { Name = "Base de données", Host = "db.example.org", Username = "postgres", Folder = "Prod", Tags = ["db"] },
        new() { Name = "Bac à sable", Host = "10.0.0.5", Username = "alex", Folder = "Tests", Notes = "machine jetable" },
        new() { Name = "Raspberry", Host = "pi.local", Username = "pi" },
        new() { Name = "Console du routeur", Protocol = ConnectionProtocol.Serial, Serial = new SerialSettings { PortName = "COM7" } },
    ];

    private static string[] Names(string? query = null, string? folder = null, string? tag = null) =>
        [.. ConnectionFilter.Apply(Profiles, query, folder, tag).Select(p => p.Name)];

    [Fact]
    public void No_criteria_returns_everything() => Assert.Equal(5, Names().Length);

    [Theory]
    [InlineData("web", "Frontal web")]
    [InlineData("WEB01", "Frontal web")]
    [InlineData("postgres", "Base de données")]
    [InlineData("donnees", "Base de données")]
    [InlineData("nginx", "Frontal web")]
    [InlineData("jetable", "Bac à sable")]
    [InlineData("10.0.0", "Bac à sable")]
    [InlineData("tests", "Bac à sable")]
    [InlineData("com7", "Console du routeur")]
    public void Query_matches_any_field_ignoring_case_and_accents(string query, string expected) =>
        Assert.Equal([expected], Names(query));

    [Fact]
    public void Every_word_of_the_query_must_match()
    {
        Assert.Equal(["Frontal web"], Names("example deploy"));
        Assert.Empty(Names("example inconnu"));
    }

    [Fact]
    public void Folder_filter_includes_subfolders() =>
        Assert.Equal(["Frontal web", "Base de données"], Names(folder: "Prod"));

    [Fact]
    public void Tag_filter_is_exact_and_case_insensitive()
    {
        Assert.Equal(["Base de données"], Names(tag: "DB"));
        Assert.Empty(Names(tag: "d"));
    }

    [Fact]
    public void Filters_combine() => Assert.Equal(["Frontal web"], Names("example", "Prod", "web"));
}
