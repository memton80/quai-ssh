using Quai.Core.Terminal;
using Renci.SshNet;

namespace Quai.Core.Ssh;

/// <summary>Shell interactif au-dessus d'un <see cref="ShellStream"/> SSH.NET.</summary>
internal sealed class SshTerminalSession : ITerminalSession
{
    public const string TerminalName = "xterm-256color";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly SshClient _client;
    private readonly ShellStream _stream;
    private readonly Lock _writeGate = new();
    private readonly Thread _reader;
    private int _closed;
    private int _started;

    public SshTerminalSession(SshClient client, ShellStream stream)
    {
        _client = client;
        _stream = stream;
        _client.ErrorOccurred += (_, e) => RaiseClosed(e.Exception);
        _stream.ErrorOccurred += (_, e) => RaiseClosed(e.Exception);

        // Lecture en boucle plutôt que par l'événement DataReceived : le tampon interne du flux est ainsi vidé.
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "Quai SSH shell" };
    }

    public event Action<byte[]>? DataReceived;

    public event Action<Exception?>? Closed;

    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _reader.Start();
        }
    }

    public void Send(byte[] data)
    {
        if (!IsOpen || data.Length == 0)
        {
            return;
        }

        try
        {
            lock (_writeGate)
            {
                _stream.Write(data, 0, data.Length);
                _stream.Flush();
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or Renci.SshNet.Common.SshException or IOException)
        {
            RaiseClosed(ex is ObjectDisposedException ? null : ex);
        }
    }

    public void Resize(int columns, int rows)
    {
        if (!IsOpen || columns <= 0 || rows <= 0)
        {
            return;
        }

        try
        {
            _stream.ChangeWindowSize((uint)columns, (uint)rows, 0, 0);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or Renci.SshNet.Common.SshException or IOException)
        {
            // Session en cours de fermeture : rien à redimensionner.
        }
    }

    public async Task<string?> QueryWorkingDirectoryAsync(CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
        {
            return null;
        }

        try
        {
            using var command = _client.CreateCommand(WorkingDirectoryProbe.Command);
            command.CommandTimeout = ProbeTimeout;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            await command.ExecuteAsync(timeout.Token).ConfigureAwait(false);
            return WorkingDirectoryProbe.Parse(command.Result);
        }
        catch (Exception ex) when (ex is Renci.SshNet.Common.SshException or IOException or ObjectDisposedException
            or OperationCanceledException or InvalidOperationException)
        {
            // Serveur qui refuse un canal de plus, connexion en train de tomber… : on ouvrira le dossier personnel.
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _closed, 1);
        return new ValueTask(Task.Run(() =>
        {
            try
            {
                _stream.Dispose();
                _client.Dispose();
            }
            catch (Exception)
            {
                // Fermeture au mieux : la connexion peut déjà être rompue.
            }
        }));
    }

    private void ReadLoop()
    {
        var buffer = new byte[32 * 1024];
        try
        {
            while (true)
            {
                var read = _stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                DataReceived?.Invoke(buffer.AsSpan(0, read).ToArray());
            }

            RaiseClosed(null);
        }
        catch (ObjectDisposedException)
        {
            RaiseClosed(null);
        }
        catch (Exception ex)
        {
            RaiseClosed(ex);
        }
    }

    private void RaiseClosed(Exception? error)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            Closed?.Invoke(error);
        }
    }
}
