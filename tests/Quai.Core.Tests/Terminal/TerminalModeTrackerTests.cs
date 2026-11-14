using Quai.Core.Terminal;

namespace Quai.Core.Tests.Terminal;

public class TerminalModeTrackerTests
{
    [Fact]
    public void Starts_with_every_mode_off()
    {
        var tracker = new TerminalModeTracker();

        Assert.False(tracker.BracketedPaste);
        Assert.False(tracker.MouseTracking);
    }

    [Fact]
    public void Follows_bracketed_paste_set_and_reset()
    {
        var tracker = new TerminalModeTracker();

        tracker.Feed("prompt$ \u001b[?2004h");
        Assert.True(tracker.BracketedPaste);

        tracker.Feed("ls\r\n\u001b[?2004l");
        Assert.False(tracker.BracketedPaste);
    }

    [Fact]
    public void Understands_a_sequence_split_between_packets()
    {
        var tracker = new TerminalModeTracker();

        tracker.Feed("abc\u001b");
        tracker.Feed("[?20");
        tracker.Feed("04");
        Assert.False(tracker.BracketedPaste);

        tracker.Feed("h");
        Assert.True(tracker.BracketedPaste);
    }

    [Fact]
    public void Applies_every_parameter_of_a_combined_sequence()
    {
        var tracker = new TerminalModeTracker();

        tracker.Feed("\u001b[?1049;1000;1006;2004h");
        Assert.True(tracker.MouseTracking);
        Assert.True(tracker.BracketedPaste);

        tracker.Feed("\u001b[?1000l");
        Assert.False(tracker.MouseTracking);
        Assert.True(tracker.BracketedPaste);
    }

    [Fact]
    public void Mouse_tracking_stays_on_while_one_mouse_mode_remains()
    {
        var tracker = new TerminalModeTracker();

        tracker.Feed("\u001b[?1000h\u001b[?1002h\u001b[?1000l");
        Assert.True(tracker.MouseTracking);

        tracker.Feed("\u001b[?1002l");
        Assert.False(tracker.MouseTracking);
    }

    [Theory]
    [InlineData("\u001b[2004h")]
    [InlineData("\u001b[?2004")]
    [InlineData("\u001b]0;?2004h\u0007")]
    [InlineData("?2004h")]
    [InlineData("\u001b[?20x04h")]
    public void Ignores_sequences_that_are_not_a_dec_private_mode(string text)
    {
        var tracker = new TerminalModeTracker();

        tracker.Feed(text);

        Assert.False(tracker.BracketedPaste);
    }

    [Fact]
    public void An_escape_inside_a_sequence_starts_a_new_one()
    {
        var tracker = new TerminalModeTracker();

        tracker.Feed("\u001b[?20\u001b[?2004h");

        Assert.True(tracker.BracketedPaste);
    }

    [Fact]
    public void Full_reset_turns_every_mode_off()
    {
        var tracker = new TerminalModeTracker();
        tracker.Feed("\u001b[?1003;2004h");

        tracker.Feed("\u001bc");

        Assert.False(tracker.BracketedPaste);
        Assert.False(tracker.MouseTracking);
    }

    [Fact]
    public void Reset_turns_every_mode_off_and_forgets_a_partial_sequence()
    {
        var tracker = new TerminalModeTracker();
        tracker.Feed("\u001b[?1000h\u001b[?2004h\u001b[?20");

        tracker.Reset();
        tracker.Feed("04h");

        Assert.False(tracker.BracketedPaste);
        Assert.False(tracker.MouseTracking);
    }
}
