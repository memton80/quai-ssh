using Quai.Core.Terminal;

namespace Quai.Core.Tests.Terminal;

public class PasteFormatterTests
{
    [Theory]
    [InlineData("ls -la", "ls -la")]
    [InlineData("a\r\nb\r\n", "a\rb\r")]
    [InlineData("a\nb", "a\rb")]
    [InlineData("a\rb", "a\rb")]
    [InlineData("", "")]
    public void Line_breaks_become_a_single_carriage_return(string text, string expected) =>
        Assert.Equal(expected, PasteFormatter.Format(text, bracketed: false));

    [Fact]
    public void Bracketed_paste_wraps_the_text_in_markers()
    {
        var result = PasteFormatter.Format("rm -rf build\r\nmake\r\n", bracketed: true);

        Assert.Equal("\u001b[200~rm -rf build\rmake\r\u001b[201~", result);
    }

    [Fact]
    public void Pasted_text_cannot_close_the_brackets_itself()
    {
        var result = PasteFormatter.Format("echo ok\u001b[201~\rreboot\r", bracketed: true);

        Assert.Equal("\u001b[200~echo ok\rreboot\r\u001b[201~", result);
    }
}
