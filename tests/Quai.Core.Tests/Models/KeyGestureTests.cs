using Quai.Core.Models;

namespace Quai.Core.Tests.Models;

public sealed class KeyGestureTests
{
    private const int F5 = 0x74;

    [Theory]
    [InlineData(true, true, false, '1', KeyGestureProblem.None)]
    [InlineData(true, true, false, 'K', KeyGestureProblem.None)]
    [InlineData(false, true, true, 'K', KeyGestureProblem.None)]
    [InlineData(false, true, true, '9', KeyGestureProblem.None)]
    [InlineData(false, true, false, F5, KeyGestureProblem.None)]
    [InlineData(true, false, false, F5, KeyGestureProblem.None)]
    [InlineData(false, false, true, F5, KeyGestureProblem.None)]
    [InlineData(false, false, false, F5, KeyGestureProblem.NeedsModifier)]
    [InlineData(false, false, false, 'A', KeyGestureProblem.NeedsModifier)]
    [InlineData(true, false, true, '2', KeyGestureProblem.AltGr)]
    [InlineData(true, true, true, 'E', KeyGestureProblem.AltGr)]
    [InlineData(true, false, false, 'R', KeyGestureProblem.ShellKey)]
    [InlineData(false, false, true, 'B', KeyGestureProblem.ShellKey)]
    [InlineData(false, true, false, 'A', KeyGestureProblem.ShellKey)]
    [InlineData(true, false, false, '3', KeyGestureProblem.ShellKey)]
    [InlineData(false, false, true, '1', KeyGestureProblem.ShellKey)]
    [InlineData(false, true, false, '1', KeyGestureProblem.ShellKey)]
    [InlineData(true, true, false, 'C', KeyGestureProblem.Reserved)]
    [InlineData(true, true, false, 'V', KeyGestureProblem.Reserved)]
    [InlineData(true, true, false, 'P', KeyGestureProblem.Reserved)]
    [InlineData(true, true, false, 'T', KeyGestureProblem.Reserved)]
    [InlineData(false, false, true, 0x73, KeyGestureProblem.Reserved)]
    [InlineData(true, true, false, 0x0D, KeyGestureProblem.UnsupportedKey)]
    [InlineData(true, true, false, 0x1B, KeyGestureProblem.UnsupportedKey)]
    public void Only_combinations_the_shell_does_not_need_are_accepted(bool control, bool shift, bool alt, int key, KeyGestureProblem expected) =>
        Assert.Equal(expected, new KeyGesture(control, shift, alt, key).Problem);

    [Theory]
    [InlineData("Ctrl+Shift+1", true, true, false, '1')]
    [InlineData("ctrl + maj + k", true, true, false, 'K')]
    [InlineData("Alt+Shift+F12", false, true, true, 0x7B)]
    [InlineData("Shift+F24", false, true, false, 0x87)]
    public void The_saved_form_is_read_back(string text, bool control, bool shift, bool alt, int key)
    {
        Assert.True(KeyGesture.TryParse(text, out var gesture));
        Assert.Equal(new KeyGesture(control, shift, alt, key), gesture);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+Shift+")]
    [InlineData("Ctrl+Shift+Enter")]
    [InlineData("Super+1")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F05")]
    [InlineData("Ctrl+é")]
    public void Unreadable_shortcuts_are_rejected(string? text) => Assert.False(KeyGesture.TryParse(text, out _));

    [Fact]
    public void Saved_and_displayed_forms_name_the_modifiers_in_a_stable_order()
    {
        var gesture = new KeyGesture(Control: true, Shift: true, Alt: false, Key: '1');

        Assert.Equal("Ctrl+Shift+1", gesture.ToString());
        Assert.Equal("Ctrl+Maj+1", gesture.Format("Ctrl", "Maj", "Alt"));
        Assert.Equal("Shift+Alt+F5", new KeyGesture(false, true, true, F5).ToString());
    }

    [Fact]
    public void A_snippet_keeps_only_a_valid_shortcut_in_its_canonical_form()
    {
        var valid = new Snippet { Command = "uptime", Shortcut = " ctrl+maj+1 " };
        var forbidden = new Snippet { Command = "uptime", Shortcut = "Ctrl+C" };
        var garbage = new Snippet { Command = "uptime", Shortcut = "n'importe quoi" };

        valid.Normalize();
        forbidden.Normalize();
        garbage.Normalize();

        Assert.Equal("Ctrl+Shift+1", valid.Shortcut);
        Assert.Null(forbidden.Shortcut);
        Assert.Null(forbidden.Gesture);
        Assert.Null(garbage.Shortcut);
    }
}
