using System.Collections.Concurrent;
using Quai.Core.Terminal;

namespace Quai.Core.Local;

/// <summary>Pseudo-console ouverte : flux du shell, taille, fin du processus (remplacée dans les tests).</summary>
internal interface IPseudoTerminal : IDisposable
{
    /// <summary>Sortie du shell (séquences VT en UTF-8).</summary>
    Stream Output { get; }

    /// <summary>Entrée du shell (frappes clavier en UTF-8).</summary>
    Stream Input { get; }

    /// <summary>Fin du processus du shell, avec son code de sortie.</summary>
    event Action<int>? Exited;

    void Resize(int columns, int rows);

    /// <summary>Termine le shell et tout ce qu'il a lancé.</summary>
    void Kill();
}

/// <summary>Le shell s'est terminé de lui-même (« exit »), avec ce code de sortie.</summary>
public sealed class LocalShellExitedException(int exitCode)
    : Exception($"Le shell s'est terminé (code {exitCode}).")
{
    public int ExitCode { get; } = exitCode;
}

/// <summary>
/// Terminal local : un shell de cet ordinateur dans une pseudo-console (ConPTY). Un fil lit la sortie du shell,
/// un autre lui transmet la saisie : un long collage ne bloque jamais l'interface.
/// </summary>
internal sealed class LocalTerminalSession : ITerminalSession
{
    /// <summary>
    /// Après la fin du shell, temps laissé à la lecture pour livrer ses dernières lignes (« exit » d'un script,
    /// message d'erreur) avant d'annoncer la fermeture.
    /// </summary>
    public static readonly TimeSpan DefaultDrainDelay = TimeSpan.FromSeconds(1);

    private readonly IPseudoTerminal _terminal;
    private readonly TimeSpan _drainDelay;
    private readonly BlockingCollection<byte[]> _outgoing = [];
    private readonly Thread _reader;
    private readonly Thread _writer;
    private readonly TaskCompletionSource _readerDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _exitCode = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _closed;
    private int _started;
    private int _disposed;

    public LocalTerminalSession(IPseudoTerminal terminal, TimeSpan? drainDelay = null)
    {
        _terminal = terminal;
        _drainDelay = drainDelay ?? DefaultDrainDelay;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "Quai local terminal reader" };
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "Quai local terminal writer" };
        _terminal.Exited += OnExited;
    }

    public event Action<byte[]>? DataReceived;

    /// <summary>
    /// Fin du shell : <c>null</c> pour une sortie normale (code 0), <see cref="LocalShellExitedException"/> sinon,
    /// toute autre exception si la pseudo-console a été perdue.
    /// </summary>
    public event Action<Exception?>? Closed;

    public bool IsOpen => Volatile.Read(ref _closed) == 0;

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
        if (data.Length == 0 || !IsOpen)
        {
            return;
        }

        try
        {
            _outgoing.Add(data);
        }
        catch (InvalidOperationException)
        {
            // Session fermée entre-temps.
        }
    }

    public void Resize(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0 || !IsOpen)
        {
            return;
        }

        try
        {
            _terminal.Resize(columns, rows);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Shell en train de se terminer : la taille n'a plus d'importance.
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        Interlocked.Exchange(ref _closed, 1);
        _terminal.Exited -= OnExited;
        _outgoing.CompleteAdding();

        // La fermeture de la pseudo-console attend que sa sortie soit lue : hors du fil d'interface, la lecture continue.
        return new ValueTask(Task.Run(() =>
        {
            try
            {
                _terminal.Kill();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Déjà terminé.
            }

            try
            {
                _terminal.Dispose();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Fermeture au mieux.
            }
        }));
    }

    private void ReadLoop()
    {
        var buffer = new byte[16384];
        try
        {
            while (true)
            {
                var read = _terminal.Output.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                DataReceived?.Invoke(buffer.AsSpan(0, read).ToArray());
            }

            // Fin de la sortie : le shell s'est terminé (son code de sortie arrive juste après), ou la pseudo-console
            // a été fermée.
            _readerDone.TrySetResult();
            _exitCode.Task.Wait(_drainDelay);
            RaiseClosed(_exitCode.Task.IsCompletedSuccessfully ? ExitError(_exitCode.Task.Result) : null);
        }
        catch (Exception ex)
        {
            _readerDone.TrySetResult();

            // Après DisposeAsync, l'erreur de lecture est attendue et RaiseClosed ne fait rien.
            RaiseClosed(ex is ObjectDisposedException ? null : ex);
        }
    }

    private void WriteLoop()
    {
        try
        {
            foreach (var data in _outgoing.GetConsumingEnumerable())
            {
                _terminal.Input.Write(data, 0, data.Length);
                _terminal.Input.Flush();
            }
        }
        catch (Exception ex)
        {
            RaiseClosed(ex);
        }
    }

    /// <summary>
    /// Le processus du shell est terminé : sa sortie est encore lue un court instant (dernières lignes), puis la
    /// session est annoncée fermée, même si la pseudo-console garde son tube ouvert.
    /// </summary>
    private void OnExited(int exitCode)
    {
        _exitCode.TrySetResult(exitCode);
        _ = Task.Run(async () =>
        {
            await Task.WhenAny(_readerDone.Task, Task.Delay(_drainDelay)).ConfigureAwait(false);
            RaiseClosed(ExitError(exitCode));
        });
    }

    private static LocalShellExitedException? ExitError(int exitCode) => exitCode == 0 ? null : new LocalShellExitedException(exitCode);

    private void RaiseClosed(Exception? error)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _outgoing.CompleteAdding();
            Closed?.Invoke(error);
        }
    }
}
