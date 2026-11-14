using System.Diagnostics;
using System.Text;
using Quai.Core.Models;
using Quai.Core.Serial;
using Quai.Core.Terminal;
using Quai.Core.Tests.Support;

namespace Quai.Core.Tests.Integration;

/// <summary>
/// Vrai port série virtuel, ouvert par System.IO.Ports : <c>socat</c> crée un pseudo-terminal dont l'autre bout est
/// un « appareil » qui renvoie tout ce qu'il reçoit (<c>cat</c>). Linux avec socat uniquement (job d'intégration du CI) ;
/// ignoré ailleurs.
/// </summary>
public sealed class SerialPortTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly TempDirectory _temp = new();
    private readonly SerialSessionFactory _factory = new();
    private Process? _device;

    public void Dispose()
    {
        StopDevice();
        _temp.Dispose();
    }

    [Fact]
    public async Task Typed_text_reaches_the_device_and_its_answer_comes_back()
    {
        var port = await StartEchoDeviceAsync();
        await using var session = await OpenAsync(port);
        var output = Collect(session);
        session.Start();

        session.Send(Encoding.ASCII.GetBytes("show version\r"));

        await WaitUntilAsync(() => output.ToString().Contains("show version", StringComparison.Ordinal), "L'écho de l'appareil n'est pas revenu.");
        Assert.True(session.IsOpen);
    }

    [Fact]
    public async Task A_large_paste_is_delivered_completely_and_in_order()
    {
        var port = await StartEchoDeviceAsync();
        await using var session = await OpenAsync(port);
        var output = Collect(session);
        session.Start();
        var text = string.Concat(Enumerable.Range(0, 400).Select(i => $"ligne {i:D3}\n"));

        foreach (var chunk in text.Chunk(64))
        {
            session.Send(Encoding.ASCII.GetBytes(chunk));
        }

        await WaitUntilAsync(() => output.Length >= text.Length, "Le collage n'est pas revenu en entier.");
        Assert.Equal(text, output.ToString());
    }

    [Fact]
    public async Task Closing_frees_the_port_for_the_next_session_without_reporting_an_error()
    {
        var port = await StartEchoDeviceAsync();
        Exception? reported = null;
        var closedEvents = 0;

        var first = await OpenAsync(port);
        first.Closed += error =>
        {
            reported = error;
            closedEvents++;
        };
        first.Start();
        await first.DisposeAsync();

        // System.IO.Ports ouvre le port en accès exclusif : il doit avoir été libéré.
        await using var second = await OpenAsync(port);
        var output = Collect(second);
        second.Start();
        second.Send(Encoding.ASCII.GetBytes("encore\r"));

        await WaitUntilAsync(() => output.ToString().Contains("encore", StringComparison.Ordinal), "Le port n'a pas pu être rouvert.");
        Assert.Equal(0, closedEvents);
        Assert.Null(reported);
    }

    [Fact]
    public async Task A_device_that_disappears_closes_the_session_with_an_error()
    {
        var port = await StartEchoDeviceAsync();
        await using var session = await OpenAsync(port);
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Closed += error => closed.TrySetResult(error);
        session.Start();

        StopDevice();

        var error = await closed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.False(session.IsOpen);
    }

    [Fact]
    public async Task A_break_the_driver_cannot_send_does_not_close_the_link()
    {
        var port = await StartEchoDeviceAsync();
        await using var session = await OpenAsync(port);
        var output = Collect(session);
        var closed = false;
        session.Closed += _ => closed = true;
        session.Start();

        session.SendBreak();
        session.Send(Encoding.UTF8.GetBytes("toujours là\r"));

        await WaitUntilAsync(() => output.ToString().Contains("toujours là", StringComparison.Ordinal), "La liaison ne répond plus après le break.");
        Assert.False(closed);
    }

    private static StringBuilder Collect(ITerminalSession session)
    {
        var output = new StringBuilder();
        session.DataReceived += data =>
        {
            lock (output)
            {
                output.Append(Encoding.UTF8.GetString(data));
            }
        };
        return output;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string failure)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private Task<ITerminalSession> OpenAsync(string port) =>
        _factory.OpenAsync(new SerialSettings { PortName = port, BaudRate = 115200 }, TestContext.Current.CancellationToken);

    /// <summary>Démarre l'appareil d'écho et retourne le chemin de son port.</summary>
    private async Task<string> StartEchoDeviceAsync()
    {
        var socat = FindSocat();
        Assert.SkipWhen(socat is null, "socat absent : pas de port série virtuel (Linux uniquement).");

        var link = _temp.File("ttyQuai");
        var start = new ProcessStartInfo(socat!)
        {
            ArgumentList = { $"pty,raw,echo=0,link={link}", "exec:cat" },
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        _device = Process.Start(start) ?? throw new InvalidOperationException("socat n'a pas démarré.");
        await WaitUntilAsync(() => File.Exists(link), "socat n'a pas créé le port virtuel.");
        return link;
    }

    private void StopDevice()
    {
        if (_device is { HasExited: false })
        {
            _device.Kill(entireProcessTree: true);
            _device.WaitForExit(5000);
        }

        _device?.Dispose();
        _device = null;
    }

    private static string? FindSocat()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, "socat"))
            .FirstOrDefault(File.Exists);
    }
}
