using System.Text;
using Quai.Core.Local;
using Quai.Core.Tests.Support;

namespace Quai.Core.Tests.Integration;

/// <summary>
/// Vrai shell local dans une pseudo-console : invite de commandes sous Windows (ConPTY de Windows Terminal), shell
/// de l'utilisateur ailleurs (pseudo-terminal POSIX, même bibliothèque).
/// </summary>
public sealed class LocalTerminalTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_real_shell_runs_commands_follows_resizes_and_reports_its_exit_code()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var shells = new LocalShellCatalog(new KeyStrings()).GetShells();
        var shell = OperatingSystem.IsWindows()
            ? shells.Single(s => s.Id == LocalShellCatalog.CommandPromptId)
            : shells[0];
        var output = new StringBuilder();
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var session = await new LocalTerminalFactory().OpenAsync(shell, 80, 24, cancellation);
        session.DataReceived += data =>
        {
            lock (output)
            {
                output.Append(Encoding.UTF8.GetString(data));
            }
        };
        session.Closed += error => closed.TrySetResult(error);
        session.Start();

        // La commande tapée s'affiche avec l'échappement (^ ou guillemets) : seul le résultat contient le marqueur.
        Type(session, OperatingSystem.IsWindows() ? "echo QUAI-LOCAL-^OK\r" : "echo QUAI-LOCAL-\"OK\"\r");
        await WaitForAsync(output, "QUAI-LOCAL-OK", cancellation);

        if (!OperatingSystem.IsWindows())
        {
            session.Resize(100, 30);
            Type(session, "stty size\r");
            await WaitForAsync(output, "30 100", cancellation);
        }

        Type(session, "exit 3\r");
        var error = Assert.IsType<LocalShellExitedException>(await closed.Task.WaitAsync(Timeout, cancellation));
        Assert.Equal(3, error.ExitCode);
        Assert.False(session.IsOpen);
    }

    [Fact]
    public async Task A_missing_shell_is_reported_with_its_name()
    {
        var shell = new Quai.Core.Models.LocalShell("absent", "Shell absent", Path.Combine(Path.GetTempPath(), $"quai-{Guid.NewGuid():N}.exe"), []);

        var error = await Assert.ThrowsAsync<LocalShellUnavailableException>(
            () => new LocalTerminalFactory().OpenAsync(shell, 80, 24, TestContext.Current.CancellationToken));

        Assert.Equal("Shell absent", error.ShellName);
    }

    private static void Type(Quai.Core.Terminal.ITerminalSession session, string text) => session.Send(Encoding.UTF8.GetBytes(text));

    private static async Task WaitForAsync(StringBuilder output, string expected, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (output)
            {
                if (output.ToString().Contains(expected, StringComparison.Ordinal))
                {
                    return;
                }
            }

            await Task.Delay(50, cancellation);
        }

        lock (output)
        {
            Assert.Fail($"« {expected} » n'apparaît pas dans la sortie du shell :\n{output}");
        }
    }
}
