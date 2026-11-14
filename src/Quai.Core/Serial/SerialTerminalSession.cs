using System.Collections.Concurrent;
using Quai.Core.Terminal;

namespace Quai.Core.Serial;

/// <summary>Liaison série ouverte : flux d'octets et commande du signal « break ».</summary>
internal interface ISerialLine : IDisposable
{
    Stream Stream { get; }

    void SetBreak(bool on);
}

/// <summary>
/// Terminal sur une liaison série. Un fil lit le port, un autre y écrit : à 9600 bauds, coller quelques
/// kilo-octets prend des secondes, et la saisie ne doit jamais bloquer l'interface.
/// </summary>
internal sealed class SerialTerminalSession : ITerminalSession
{
    /// <summary>Durée du « break » : 2/5 s comme PuTTY (POSIX demande entre 1/4 et 1/2 s).</summary>
    public static readonly TimeSpan DefaultBreakDuration = TimeSpan.FromMilliseconds(400);

    private readonly ISerialLine _line;
    private readonly Stream _stream;
    private readonly TimeSpan _breakDuration;
    private readonly BlockingCollection<byte[]?> _outgoing = [];
    private readonly Thread _reader;
    private readonly Thread _writer;
    private int _closed;
    private int _started;
    private int _disposed;

    public SerialTerminalSession(ISerialLine line, TimeSpan? breakDuration = null)
    {
        _line = line;
        _stream = line.Stream;
        _breakDuration = breakDuration ?? DefaultBreakDuration;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "Quai serial reader" };
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "Quai serial writer" };
    }

    public event Action<byte[]>? DataReceived;

    public event Action<Exception?>? Closed;

    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    public bool CanSendBreak => true;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _reader.Start();
            _writer.Start();
        }
    }

    public void Send(byte[] data)
    {
        if (data.Length > 0)
        {
            Enqueue(data);
        }
    }

    /// <summary>Pas de taille de fenêtre sur une liaison série.</summary>
    public void Resize(int columns, int rows)
    {
    }

    /// <summary>Mis en file avec la saisie : le break part après ce qui a été tapé avant lui.</summary>
    public void SendBreak() => Enqueue(null);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        Interlocked.Exchange(ref _closed, 1);
        _outgoing.CompleteAdding();
        return new ValueTask(Task.Run(() =>
        {
            try
            {
                // Fermer le port débloque la lecture (et l'écriture) en cours.
                _line.Dispose();
            }
            catch (Exception)
            {
                // Fermeture au mieux : après un débranchement, System.IO.Ports peut lever en fermant.
            }
        }));
    }

    private void Enqueue(byte[]? item)
    {
        if (!IsOpen)
        {
            return;
        }

        try
        {
            _outgoing.Add(item);
        }
        catch (InvalidOperationException)
        {
            // Session fermée entre-temps.
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                int read;
                try
                {
                    read = _stream.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    // Délai « infini » de System.IO.Ports (environ 24 jours) écoulé sans rien recevoir.
                    continue;
                }

                if (read <= 0)
                {
                    // Une liaison série ne se termine pas d'elle-même : fin de flux = appareil disparu.
                    RaiseClosed(new EndOfStreamException("Le port série a été fermé."));
                    return;
                }

                DataReceived?.Invoke(buffer.AsSpan(0, read).ToArray());
            }
        }
        catch (Exception ex)
        {
            // Après DisposeAsync, l'erreur de lecture est attendue et RaiseClosed ne fait rien.
            RaiseClosed(ex);
        }
    }

    private void WriteLoop()
    {
        try
        {
            foreach (var data in _outgoing.GetConsumingEnumerable())
            {
                if (data is null)
                {
                    SendBreakSignal();
                }
                else
                {
                    _stream.Write(data, 0, data.Length);
                    _stream.Flush();
                }
            }
        }
        catch (Exception ex)
        {
            RaiseClosed(ex);
        }
    }

    private void SendBreakSignal()
    {
        try
        {
            _line.SetBreak(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            // Pilote sans « break » (port virtuel) : la liaison continue ; un débranchement sera vu par la lecture.
            return;
        }

        Thread.Sleep(_breakDuration);
        _line.SetBreak(false);
    }

    private void RaiseClosed(Exception? error)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _outgoing.CompleteAdding();
            Closed?.Invoke(error);
        }
    }
}
