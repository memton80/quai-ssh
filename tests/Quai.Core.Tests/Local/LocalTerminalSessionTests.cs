using System.Collections.Concurrent;
using System.Text;
using Quai.Core.Local;

namespace Quai.Core.Tests.Local;

public sealed class LocalTerminalSessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Output_and_input_flow_both_ways_and_resize_reaches_the_console()
    {
        var console = new FakePseudoTerminal();
        await using var session = new LocalTerminalSession(console);
        var received = new BlockingCollection<string>();
        session.DataReceived += data => received.Add(Encoding.UTF8.GetString(data));
        session.Start();

        console.Print("PS C:\\Users\\alex> ");
        session.Send(Encoding.UTF8.GetBytes("dir\r"));
        session.Resize(120, 40);
        session.Resize(0, 0);

        Assert.True(received.TryTake(out var text, Timeout));
        Assert.Equal("PS C:\\Users\\alex> ", text);
        Assert.True(console.Typed.TryTake(out var typed, Timeout));
        Assert.Equal("dir\r", typed);
        Assert.Equal([(120, 40)], console.Sizes);
        Assert.True(session.IsOpen);
    }

    [Fact]
    public async Task Exit_with_code_zero_is_a_normal_close_delivered_after_the_last_output()
    {
        var console = new FakePseudoTerminal();
        await using var session = new LocalTerminalSession(console, drainDelay: TimeSpan.FromSeconds(5));
        var output = new StringBuilder();
        var closed = new TaskCompletionSource<Exception?>();
        session.DataReceived += data =>
        {
            lock (output)
            {
                output.Append(Encoding.UTF8.GetString(data));
            }
        };
        session.Closed += error => closed.TrySetResult(error);
        session.Start();

        console.Exit(0);
        console.Print("au revoir");
        console.EndOutput();

        Assert.Null(await closed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.Equal("au revoir", output.ToString());
        Assert.False(session.IsOpen);
    }

    [Fact]
    public async Task A_non_zero_exit_code_is_reported_even_when_the_output_ends_first()
    {
        var console = new FakePseudoTerminal();
        await using var session = new LocalTerminalSession(console, drainDelay: TimeSpan.FromSeconds(5));
        var closed = new TaskCompletionSource<Exception?>();
        session.Closed += error => closed.TrySetResult(error);
        session.Start();

        console.EndOutput();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        console.Exit(3);

        var error = Assert.IsType<LocalShellExitedException>(await closed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.Equal(3, error.ExitCode);
    }

    [Fact]
    public async Task A_console_that_keeps_its_pipe_open_after_exit_is_still_reported_closed()
    {
        var console = new FakePseudoTerminal();
        await using var session = new LocalTerminalSession(console, drainDelay: TimeSpan.FromMilliseconds(50));
        var closed = new TaskCompletionSource<Exception?>();
        session.Closed += error => closed.TrySetResult(error);
        session.Start();

        console.Exit(0);

        Assert.Null(await closed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Dispose_kills_the_shell_and_raises_no_close_event()
    {
        var console = new FakePseudoTerminal();
        var session = new LocalTerminalSession(console);
        var closedEvents = 0;
        session.Closed += _ => Interlocked.Increment(ref closedEvents);
        session.Start();

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, console.Kills);
        Assert.True(console.Disposed);
        Assert.False(session.IsOpen);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(0, Volatile.Read(ref closedEvents));
        session.Send([1, 2, 3]);
    }

    /// <summary>Pseudo-console simulée : la sortie est un flux alimenté par le test, l'entrée est enregistrée.</summary>
    private sealed class FakePseudoTerminal : IPseudoTerminal
    {
        private readonly QueueStream _output = new();
        private readonly RecordingStream _input;

        public FakePseudoTerminal()
        {
            _input = new RecordingStream(Typed);
        }

        public event Action<int>? Exited;

        public BlockingCollection<string> Typed { get; } = [];

        public List<(int Columns, int Rows)> Sizes { get; } = [];

        public int Kills { get; private set; }

        public bool Disposed { get; private set; }

        public Stream Output => _output;

        public Stream Input => _input;

        public void Print(string text) => _output.Push(Encoding.UTF8.GetBytes(text));

        public void EndOutput() => _output.Complete();

        public void Exit(int code) => Exited?.Invoke(code);

        public void Resize(int columns, int rows) => Sizes.Add((columns, rows));

        public void Kill()
        {
            Kills++;
            _output.Complete();
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class QueueStream : Stream
    {
        private readonly BlockingCollection<byte[]> _chunks = [];

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public void Push(byte[] data) => _chunks.Add(data);

        public void Complete() => _chunks.CompleteAdding();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_chunks.TryTake(out var chunk, System.Threading.Timeout.Infinite))
            {
                return 0;
            }

            chunk.CopyTo(buffer, offset);
            return chunk.Length;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RecordingStream(BlockingCollection<string> written) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => written.Add(Encoding.UTF8.GetString(buffer, offset, count));
    }
}
