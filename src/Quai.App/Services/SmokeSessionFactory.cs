using System.Text;
using Quai.Core.Models;
using Quai.Core.Serial;
using Quai.Core.Sftp;
using Quai.Core.Ssh;
using Quai.Core.Terminal;
using Quai.Core.Updates;

namespace Quai.App.Services;

/// <summary>
/// Sessions de démonstration du test de fumée : aucun réseau ni port série, une bannière puis l'écho de la saisie.
/// Permet de vérifier sur le CI toute la chaîne du terminal (moteur Windows Terminal) sans serveur SSH ni appareil.
/// </summary>
internal sealed class SmokeSessionFactory : ISshSessionFactory, ISerialSessionFactory, ISerialPortCatalog
{
    /// <summary>Version « publiée » du test de fumée, forcément plus récente : le bandeau doit apparaître.</summary>
    public const string ReleaseTag = "v99.0.0";

    public const string Banner = "Quai - terminal de test";

    public const string SerialBanner = "Quai - port série de test";

    /// <summary>Réponse de l'appareil simulé à un signal « break ».</summary>
    public const string BreakAnswer = "[break reçu]";

    /// <summary>Ports « détectés » proposés par la fiche de connexion.</summary>
    public static readonly IReadOnlyList<string> SerialPorts = ["COM1", "COM3"];

    private int _liveSessions;

    public Task<ITerminalSession> OpenTerminalAsync(
        ConnectionProfile profile,
        int columns,
        int rows,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ITerminalSession>(new EchoSession(
            $"\u001b[1;36m{Banner}\u001b[0m ({columns}x{rows})\r\n" +
            "Couleurs : \u001b[31mrouge\u001b[0m \u001b[32mvert\u001b[0m \u001b[33mjaune\u001b[0m \u001b[34mbleu\u001b[0m — accents : é è à ç\r\n" +
            $"\u001b[32m{profile.Username}@{profile.Host}\u001b[0m:\u001b[34m~\u001b[0m$ ",
            Track()));

    public Task<ITerminalSession> OpenAsync(SerialSettings settings, CancellationToken cancellationToken = default) =>
        Task.FromResult<ITerminalSession>(new EchoSession(
            $"\u001b[1;33m{SerialBanner}\u001b[0m ({settings.Summary})\r\nrouter> ",
            Track(),
            breakAnswer: $"\r\n{BreakAnswer}\r\nrommon 1 > "));

    public IReadOnlyList<string> GetPortNames() => SerialPorts;

    public Task<ISftpSession> OpenSftpAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        Task.FromResult<ISftpSession>(new DemoSftpSession(Track()));

    /// <summary>Sessions de démonstration ouvertes et pas encore fermées (test de la fermeture de l'application).</summary>
    public int LiveSessions => Volatile.Read(ref _liveSessions);

    /// <summary>Compte une session ouverte ; l'action retournée la décompte à sa fermeture.</summary>
    private Action Track()
    {
        Interlocked.Increment(ref _liveSessions);
        return () => Interlocked.Decrement(ref _liveSessions);
    }

    /// <summary>Petit serveur de fichiers en mémoire : de quoi remplir le panneau distant et simuler un transfert.</summary>
    private sealed class DemoSftpSession(Action onClosed) : ISftpSession
    {
        private const string Home = "/home/quai";
        private static readonly DateTime Stamp = new(2026, 11, 2, 9, 41, 0, DateTimeKind.Local);

        private readonly List<RemoteEntry> _entries =
        [
            new("sauvegardes", Home + "/sauvegardes", true, false, 0, Stamp, 0b111_101_101),
            new("www", Home + "/www", true, false, 0, Stamp, 0b111_101_101),
            new("courant", Home + "/courant", true, true, 0, Stamp, 0b111_111_111),
            new("deploy.sh", Home + "/deploy.sh", false, false, 2_348, Stamp, 0b111_101_000),
            new("journal.log", Home + "/journal.log", false, false, 18_734_112, Stamp, 0b110_100_100),
            new("notes.txt", Home + "/notes.txt", false, false, 412, Stamp, 0b110_100_100),
        ];

        public event Action<Exception?>? Closed;

        public bool IsConnected { get; private set; } = true;

        public string HomeDirectory => Home;

        public Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteEntry>>(path == Home ? [.. _entries] : []);

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(path == Home || _entries.Any(e => e.FullPath == path));

        public async Task UploadFileAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
        {
            var size = new FileInfo(localPath).Length;
            for (var step = 1; step <= 4; step++)
            {
                await Task.Delay(150, cancellationToken);
                progress?.Report(size * step / 4);
            }

            _entries.RemoveAll(e => e.FullPath == remotePath);
            _entries.Add(new RemoteEntry(RemotePath.GetName(remotePath), remotePath, false, false, size, DateTime.Now, 0b110_100_100));
        }

        public Task DownloadFileAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
            File.WriteAllTextAsync(localPath, string.Empty, cancellationToken);

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RenameLinkAsync(string oldPath, string newPath, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteLinkAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ChangePermissionsAsync(string path, int mode, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetLastWriteTimeAsync(string path, DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            if (IsConnected)
            {
                IsConnected = false;
                onClosed();
                Closed?.Invoke(null);
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Affiche une bannière puis renvoie la saisie ; <paramref name="breakAnswer"/> : sait recevoir un « break ».</summary>
    private sealed class EchoSession(string banner, Action onClosed, string? breakAnswer = null) : ITerminalSession
    {
        public event Action<byte[]>? DataReceived;

        public event Action<Exception?>? Closed;

        public bool IsOpen { get; private set; } = true;

        public bool CanSendBreak => breakAnswer is not null;

        public void Start() => Emit(banner);

        public void Send(byte[] data) => Emit(Encoding.UTF8.GetString(data).Replace("\r", "\r\n"));

        public void Resize(int columns, int rows)
        {
        }

        public void SendBreak()
        {
            if (breakAnswer is not null)
            {
                Emit(breakAnswer);
            }
        }

        public ValueTask DisposeAsync()
        {
            if (IsOpen)
            {
                IsOpen = false;
                onClosed();
                Closed?.Invoke(null);
            }

            return ValueTask.CompletedTask;
        }

        private void Emit(string text) => DataReceived?.Invoke(Encoding.UTF8.GetBytes(text));
    }
}

/// <summary>Source de releases du test de fumée : aucune requête réseau.</summary>
internal sealed class SmokeReleaseSource : IReleaseSource
{
    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ReleaseInfo?>(new ReleaseInfo(SmokeSessionFactory.ReleaseTag, []));
}
