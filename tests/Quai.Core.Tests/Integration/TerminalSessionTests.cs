using System.Text;
using Quai.Core.Ssh;
using Quai.Core.Tests.Support;

namespace Quai.Core.Tests.Integration;

/// <summary>Shell interactif réel sur le serveur OpenSSH de test.</summary>
public sealed class TerminalSessionTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SshSessionFactory _factory;

    public TerminalSessionTests()
    {
        var prompts = new ScriptedPrompts();
        _factory = new SshSessionFactory(
            new SshConnector(new KnownHostsStore(_temp.File("known_hosts")), new InMemorySecretStore(), prompts, prompts));
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task The_shell_echoes_commands_follows_resizes_and_closes_on_exit()
    {
        SshTestServer.SkipUnlessAvailable();
        var cancellation = TestContext.Current.CancellationToken;
        var output = new StringBuilder();
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var session = await _factory.OpenTerminalAsync(
            SshTestServer.KeyProfile(SshTestServer.KeyFile), 80, 24, cancellation);
        session.DataReceived += data =>
        {
            lock (output)
            {
                output.Append(Encoding.UTF8.GetString(data));
            }
        };
        session.Closed += error => closed.TrySetResult(error);
        session.Start();

        // Le calcul prouve que la sortie vient bien du shell distant et non de l'écho de la saisie.
        session.Send(Encoding.UTF8.GetBytes("echo quai-$((6*7))-é\n"));
        await WaitForAsync(output, "quai-42-é", cancellation);

        session.Send(Encoding.UTF8.GetBytes("echo $TERM; stty size\n"));
        await WaitForAsync(output, "24 80", cancellation);
        Assert.Contains(SshTerminalSession.TerminalName, Snapshot(output));

        session.Resize(132, 43);
        session.Send(Encoding.UTF8.GetBytes("stty size\n"));
        await WaitForAsync(output, "43 132", cancellation);

        Assert.True(session.IsOpen);
        session.Send(Encoding.UTF8.GetBytes("exit\n"));
        var error = await closed.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellation);

        Assert.Null(error);
        Assert.False(session.IsOpen);
    }

    [Fact]
    public async Task Disposing_the_session_does_not_report_an_error()
    {
        SshTestServer.SkipUnlessAvailable();
        var cancellation = TestContext.Current.CancellationToken;
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = await _factory.OpenTerminalAsync(SshTestServer.KeyProfile(SshTestServer.KeyFile), 80, 24, cancellation);
        session.Closed += error => closed.TrySetResult(error);
        session.Start();

        await session.DisposeAsync();
        session.Send([0x0D]);
        session.Resize(10, 10);

        Assert.False(session.IsOpen);
        Assert.False(closed.Task.IsCompleted && closed.Task.Result is not null);
    }

    private static string Snapshot(StringBuilder output)
    {
        lock (output)
        {
            return output.ToString();
        }
    }

    private static async Task WaitForAsync(StringBuilder output, string expected, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!Snapshot(output).Contains(expected, StringComparison.Ordinal))
        {
            Assert.True(DateTime.UtcNow < deadline, $"« {expected} » absent de la sortie du shell :\n{Snapshot(output)}");
            await Task.Delay(50, cancellation);
        }
    }
}
