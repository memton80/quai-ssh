using Quai.Core.Sftp;

namespace Quai.Core.Tests.Sftp;

public class RemotePathTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData("/home/alice", "/home/alice")]
    [InlineData("/home//alice/", "/home/alice")]
    [InlineData("/home/alice/../bob/./docs", "/home/bob/docs")]
    [InlineData("/../..", "/")]
    public void Normalize_resolves_dots_and_duplicate_slashes(string path, string expected) =>
        Assert.Equal(expected, RemotePath.Normalize(path));

    [Theory]
    [InlineData("docs", "/home/alice", "/home/alice/docs")]
    [InlineData("../bob", "/home/alice", "/home/bob")]
    [InlineData("/etc", "/home/alice", "/etc")]
    [InlineData(null, "/home/alice", "/home/alice")]
    public void Normalize_resolves_relative_paths_against_the_base(string? path, string baseDirectory, string expected) =>
        Assert.Equal(expected, RemotePath.Normalize(path, baseDirectory));

    [Theory]
    [InlineData("/", "etc", "/etc")]
    [InlineData("/home", "alice", "/home/alice")]
    [InlineData("/home/", "alice", "/home/alice")]
    public void Combine_adds_a_single_separator(string directory, string name, string expected) =>
        Assert.Equal(expected, RemotePath.Combine(directory, name));

    [Theory]
    [InlineData("/", null)]
    [InlineData("/etc", "/")]
    [InlineData("/home/alice", "/home")]
    [InlineData("/home/alice/", "/home")]
    public void GetParent_stops_at_the_root(string path, string? expected) =>
        Assert.Equal(expected, RemotePath.GetParent(path));

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/home/alice", "alice")]
    [InlineData("/home/alice/notes.txt", "notes.txt")]
    public void GetName_returns_the_last_segment(string path, string expected) =>
        Assert.Equal(expected, RemotePath.GetName(path));

    [Theory]
    [InlineData("notes.txt", true)]
    [InlineData("avec espace", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    public void IsValidName_rejects_separators_and_dot_names(string name, bool expected) =>
        Assert.Equal(expected, RemotePath.IsValidName(name));
}

public class UnixPermissionsTests
{
    [Theory]
    [InlineData(0b111_101_101, false, false, "-rwxr-xr-x", "755")]
    [InlineData(0b110_100_100, false, false, "-rw-r--r--", "644")]
    [InlineData(0b111_000_000, true, false, "drwx------", "700")]
    [InlineData(0b111_111_111, false, true, "lrwxrwxrwx", "777")]
    [InlineData(0, false, false, "----------", "000")]
    public void Modes_are_rendered_in_symbolic_and_octal_form(int mode, bool isDirectory, bool isLink, string symbolic, string octal)
    {
        Assert.Equal(symbolic, UnixPermissions.ToSymbolic(mode, isDirectory, isLink));
        Assert.Equal(octal, UnixPermissions.ToOctal(mode));
    }

    [Theory]
    [InlineData("755", 0b111_101_101)]
    [InlineData("0644", 0b110_100_100)]
    [InlineData(" 600 ", 0b110_000_000)]
    public void Octal_text_is_parsed(string text, int expected)
    {
        Assert.True(UnixPermissions.TryParseOctal(text, out var mode));
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("75")]
    [InlineData("758")]
    [InlineData("rwx")]
    [InlineData("4755")]
    [InlineData("07555")]
    public void Invalid_octal_text_is_rejected(string text) =>
        Assert.False(UnixPermissions.TryParseOctal(text, out _));

    [Fact]
    public void SshNet_expects_the_octal_digits_as_a_decimal_number()
    {
        Assert.Equal(755, UnixPermissions.ToSshNetMode(0b111_101_101));
        Assert.Equal(40, UnixPermissions.ToSshNetMode(0b000_100_000));
    }

    [Fact]
    public void Special_bits_are_ignored() =>
        Assert.Equal("755", UnixPermissions.ToOctal(0b100_111_101_101));
}
