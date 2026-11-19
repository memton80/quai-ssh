using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Porta.Pty;
using Quai.Core.Models;
using Quai.Core.Terminal;

namespace Quai.Core.Local;

/// <summary>Lance un shell local dans une pseudo-console.</summary>
public interface ILocalTerminalFactory
{
    Task<ITerminalSession> OpenAsync(LocalShell shell, int columns, int rows, CancellationToken cancellationToken = default);
}

/// <summary>Le shell n'a pas pu être lancé (fichier disparu, accès refusé, Windows trop ancien pour ConPTY…).</summary>
public sealed class LocalShellUnavailableException(string shellName, Exception inner)
    : Exception(inner.Message, inner)
{
    public string ShellName { get; } = shellName;
}

/// <summary>
/// Pseudo-console Windows (ConPTY) fournie par Porta.Pty, qui embarque la version hors boîte de Windows Terminal
/// (<c>conpty.dll</c> + <c>OpenConsole.exe</c>) et revient à celle de Windows si elle manque. Le shell et tout ce qu'il
/// lance sont rattachés à un job Windows : fermer l'onglet ne laisse aucun processus derrière lui.
/// </summary>
public sealed class LocalTerminalFactory : ILocalTerminalFactory
{
    private readonly ILogger _logger;

    public LocalTerminalFactory(ILogger<LocalTerminalFactory>? logger = null)
    {
        _logger = logger ?? NullLogger<LocalTerminalFactory>.Instance;
    }

    public async Task<ITerminalSession> OpenAsync(LocalShell shell, int columns, int rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shell);
        var options = new PtyOptions
        {
            Name = "Quai",
            App = shell.Path,
            CommandLine = [.. shell.Arguments],
            Cwd = StartDirectory(),
            Cols = Math.Max(columns, 1),
            Rows = Math.Max(rows, 1),
            Environment = new Dictionary<string, string>(),
        };

        // Shell désinstallé depuis la détection : message clair plutôt qu'un shell qui s'arrête aussitôt (POSIX).
        if (Path.IsPathRooted(shell.Path) && !File.Exists(shell.Path))
        {
            throw new LocalShellUnavailableException(shell.Name, new FileNotFoundException($"Fichier introuvable : {shell.Path}", shell.Path));
        }

        IPtyConnection connection;
        try
        {
            // Création du processus et de la pseudo-console : hors du fil d'interface.
            connection = await Task.Run(() => PtyProvider.SpawnAsync(options, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Lancement du shell local {Shell} impossible ({Error})", shell.Id, ex.GetType().Name);
            throw new LocalShellUnavailableException(shell.Name, ex);
        }

        _logger.LogInformation("Terminal local ouvert : {Shell} (processus {Pid})", shell.Id, connection.Pid);
        return new LocalTerminalSession(new PortaPseudoTerminal(connection));
    }

    /// <summary>Dossier personnel de l'utilisateur, comme Windows Terminal.</summary>
    private static string StartDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(home) ? home : Environment.CurrentDirectory;
    }

    private sealed class PortaPseudoTerminal : IPseudoTerminal
    {
        private readonly IPtyConnection _connection;

        public PortaPseudoTerminal(IPtyConnection connection)
        {
            _connection = connection;
            _connection.ProcessExited += OnProcessExited;
        }

        public event Action<int>? Exited;

        public Stream Output => _connection.ReaderStream;

        public Stream Input => _connection.WriterStream;

        public void Resize(int columns, int rows) => _connection.Resize(columns, rows);

        public void Kill() => _connection.Kill();

        public void Dispose()
        {
            _connection.ProcessExited -= OnProcessExited;
            _connection.Dispose();
        }

        private void OnProcessExited(object? sender, PtyExitedEventArgs e) => Exited?.Invoke(e.ExitCode);
    }
}
