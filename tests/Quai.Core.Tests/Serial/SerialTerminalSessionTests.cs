using System.Collections.Concurrent;
using System.Text;
using Quai.Core.Serial;

namespace Quai.Core.Tests.Serial;

public sealed class SerialTerminalSessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Data_flows_both_ways()
    {
        var line = new FakeSerialLine();
        await using var session = new SerialTerminalSession(line);
        var received = new BlockingCollection<string>();
        session.DataReceived += data => received.Add(Encoding.ASCII.GetString(data));
        session.Start();

        line.Receive("Router>");
        session.Send(Encoding.ASCII.GetBytes("enable\r"));

        Assert.True(received.TryTake(out var text, Timeout));
        Assert.Equal("Router>", text);
        Assert.Equal("write:enable\r", line.NextEvent());
        Assert.True(session.IsOpen);
        Assert.True(session.CanSendBreak);
    }

    [Fact]
    public async Task A_slow_link_never_blocks_the_caller()
    {
        var line = new FakeSerialLine { WriteGate = new ManualResetEventSlim(false) };
        await using var session = new SerialTerminalSession(line);
        session.Start();

        var sending = Task.Run(
            () =>
            {
                for (var i = 0; i < 100; i++)
                {
                    session.Send([(byte)'x']);
                }
            },
            TestContext.Current.CancellationToken);

        // Send ne doit pas attendre la fin de l'écriture sur le port.
        await sending.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        line.WriteGate.Set();
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal("write:x", line.NextEvent());
        }
    }

    [Fact]
    public async Task Break_holds_the_line_then_releases_it_in_order_with_the_input()
    {
        var line = new FakeSerialLine();
        await using var session = new SerialTerminalSession(line, breakDuration: TimeSpan.FromMilliseconds(30));
        session.Start();

        session.Send(Encoding.ASCII.GetBytes("a"));
        session.SendBreak();
        session.Send(Encoding.ASCII.GetBytes("b"));

        Assert.Equal("write:a", line.NextEvent());
        Assert.Equal("break:on", line.NextEvent());
        Assert.Equal("break:off", line.NextEvent());
        Assert.Equal("write:b", line.NextEvent());
    }

    [Fact]
    public async Task A_driver_without_break_support_keeps_the_link_open()
    {
        var line = new FakeSerialLine { BreakFails = true };
        await using var session = new SerialTerminalSession(line);
        session.Start();

        session.SendBreak();
        session.Send(Encoding.ASCII.GetBytes("a"));

        Assert.Equal("write:a", line.NextEvent());
        Assert.True(session.IsOpen);
    }

    [Fact]
    public async Task Unplugging_the_device_closes_the_session_with_an_error()
    {
        var line = new FakeSerialLine();
        await using var session = new SerialTerminalSession(line);
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Closed += error => closed.TrySetResult(error);
        session.Start();

        line.Unplug();

        var error = await closed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.IsType<IOException>(error);
        Assert.False(session.IsOpen);
    }

    [Fact]
    public async Task End_of_stream_is_reported_as_a_lost_device()
    {
        var line = new FakeSerialLine();
        await using var session = new SerialTerminalSession(line);
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Closed += error => closed.TrySetResult(error);
        session.Start();

        line.EndOfStream();

        Assert.IsType<EndOfStreamException>(await closed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_read_timeout_is_not_a_disconnection()
    {
        var line = new FakeSerialLine();
        await using var session = new SerialTerminalSession(line);
        var received = new BlockingCollection<string>();
        var closed = false;
        session.DataReceived += data => received.Add(Encoding.ASCII.GetString(data));
        session.Closed += _ => closed = true;
        session.Start();

        line.TimeOut();
        line.Receive("ok");

        Assert.True(received.TryTake(out var text, Timeout));
        Assert.Equal("ok", text);
        Assert.False(closed);
    }

    [Fact]
    public async Task Disposing_frees_the_port_without_reporting_an_error_even_if_closing_fails()
    {
        var line = new FakeSerialLine { FailOnDispose = true };
        var session = new SerialTerminalSession(line);
        var closedEvents = 0;
        session.Closed += _ => Interlocked.Increment(ref closedEvents);
        session.Start();

        await session.DisposeAsync();
        await session.DisposeAsync();
        session.Send([1]);
        session.SendBreak();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.True(line.Disposed);
        Assert.False(session.IsOpen);
        Assert.Equal(0, closedEvents);
        Assert.Null(line.NextEvent(TimeSpan.FromMilliseconds(50)));
    }

    /// <summary>Port série factice : lecture bloquante comme un vrai port, journal ordonné des écritures et des breaks.</summary>
    private sealed class FakeSerialLine : ISerialLine
    {
        private readonly BlockingCollection<Func<byte[]?>> _incoming = [];
        private readonly BlockingCollection<string> _events = [];

        public FakeSerialLine()
        {
            Stream = new LineStream(this);
        }

        public Stream Stream { get; }

        /// <summary>Si renseigné, chaque écriture attend ce signal (liaison lente ou contrôle de flux).</summary>
        public ManualResetEventSlim? WriteGate { get; init; }

        public bool FailOnDispose { get; init; }

        public bool BreakFails { get; init; }

        public bool Disposed { get; private set; }

        public void Receive(string text) => _incoming.Add(() => Encoding.ASCII.GetBytes(text));

        public void Unplug() => _incoming.Add(() => throw new IOException("Le périphérique ne reconnaît pas la commande."));

        public void EndOfStream() => _incoming.Add(() => null);

        public void TimeOut() => _incoming.Add(() => throw new TimeoutException());

        public string? NextEvent(TimeSpan? timeout = null) =>
            _events.TryTake(out var item, timeout ?? TimeSpan.FromSeconds(10)) ? item : null;

        public void SetBreak(bool on)
        {
            if (BreakFails)
            {
                throw new IOException("Fonction incorrecte.");
            }

            _events.Add(on ? "break:on" : "break:off");
        }

        public void Dispose()
        {
            Disposed = true;
            _incoming.CompleteAdding();
            if (FailOnDispose)
            {
                throw new IOException("Accès refusé.");
            }
        }

        private sealed class LineStream(FakeSerialLine line) : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                Func<byte[]?> next;
                try
                {
                    next = line._incoming.Take();
                }
                catch (InvalidOperationException)
                {
                    // Port fermé pendant la lecture, comme System.IO.Ports.
                    throw new ObjectDisposedException("port");
                }

                var data = next();
                if (data is null)
                {
                    return 0;
                }

                data.CopyTo(buffer, offset);
                return data.Length;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                line.WriteGate?.Wait();
                line._events.Add("write:" + Encoding.ASCII.GetString(buffer, offset, count));
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
