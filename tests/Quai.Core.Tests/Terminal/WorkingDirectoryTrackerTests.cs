using System.Text;
using Quai.Core.Ssh;
using Quai.Core.Terminal;

namespace Quai.Core.Tests.Terminal;

public class WorkingDirectoryTrackerTests
{
    private static string? Track(params string[] chunks)
    {
        var tracker = new WorkingDirectoryTracker();
        foreach (var chunk in chunks)
        {
            tracker.Feed(Encoding.UTF8.GetBytes(chunk));
        }

        return tracker.Current;
    }

    [Theory]
    [InlineData("\u001b]7;file://serveur/var/log\u0007", "/var/log")]
    [InlineData("\u001b]7;file:///srv/www\u001b\\", "/srv/www")]
    [InlineData("\u001b]7;file://serveur/home/al%20ice/r%C3%A9sum%C3%A9\u0007", "/home/al ice/résumé")]
    [InlineData("\u001b]1337;CurrentDir=/opt/app\u0007", "/opt/app")]
    [InlineData("texte \u001b[32mvert\u001b[0m \u001b]0;titre\u0007 \u001b]7;file://h/etc\u0007$ ", "/etc")]
    public void Announced_directories_are_read(string stream, string expected)
    {
        Assert.Equal(expected, Track(stream));
    }

    [Theory]
    [InlineData("\u001b]0;user@host: /var/log\u0007")]
    [InlineData("\u001b]7;file://serveur\u0007")]
    [InlineData("\u001b]7;http://serveur/var\u0007")]
    [InlineData("\u001b]1337;CurrentDir=relatif\u0007")]
    [InlineData("\u001b]7;file://h/var/log")]
    [InlineData("pas de séquence /var/log")]
    public void Anything_else_is_ignored(string stream)
    {
        Assert.Null(Track(stream));
    }

    [Fact]
    public void A_sequence_split_between_packets_is_reassembled()
    {
        Assert.Equal("/var/lib", Track("\u001b", "]7;fi", "le://h/var", "/lib\u001b", "\\"));
    }

    [Fact]
    public void The_last_announced_directory_wins_and_reset_forgets_it()
    {
        var tracker = new WorkingDirectoryTracker();
        tracker.Feed(Encoding.UTF8.GetBytes("\u001b]7;file://h/a\u0007\u001b]7;file://h/b\u0007"));
        Assert.Equal("/b", tracker.Current);

        tracker.Reset();

        Assert.Null(tracker.Current);
    }

    [Fact]
    public void An_interrupted_sequence_does_not_swallow_the_next_one()
    {
        Assert.Equal("/ok", Track("\u001b]7;file://h/coupé\u001b[0m\u001b]7;file://h/ok\u0007"));
    }

    [Fact]
    public void An_endless_sequence_is_dropped()
    {
        Assert.Null(Track("\u001b]7;file://h/" + new string('a', 10_000) + "\u0007"));
        Assert.Equal("/x", Track("\u001b]7;file://h/" + new string('a', 10_000), "\u001b]7;file://h/x\u0007"));
    }

    [Theory]
    [InlineData("QUAI_CWD=/usr/share\n", "/usr/share")]
    [InlineData("Bienvenue !\nQUAI_CWD=/srv/mon dossier\n", "/srv/mon dossier")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("QUAI_CWD=\n", null)]
    [InlineData("QUAI_CWD=relatif\n", null)]
    [InlineData("/var/log\n", null)]
    public void The_probe_output_is_parsed(string? output, string? expected)
    {
        Assert.Equal(expected, WorkingDirectoryProbe.Parse(output));
    }
}
