using System.Text;
using Quai.Core.Models;
using Quai.Core.Sftp;
using Quai.Core.Ssh;

namespace Quai.App.Services;

/// <summary>
/// Session de démonstration du test de fumée : aucun réseau, une bannière puis l'écho de la saisie.
/// Permet de vérifier sur le CI toute la chaîne WebView2 / xterm.js sans serveur SSH.
/// </summary>
internal sealed class SmokeSessionFactory : ISshSessionFactory
{
    public const string Banner = "Quai - terminal de test";

    public Task<ITerminalSession> OpenTerminalAsync(
        ConnectionProfile profile,
        int columns,
        int rows,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ITerminalSession>(new EchoSession(profile, columns, rows));

    public Task<ISftpSession> OpenSftpAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        Task.FromResult<ISftpSession>(new DemoSftpSession());

    /// <summary>Petit serveur de fichiers en mémoire : de quoi remplir le panneau distant et simuler un transfert.</summary>
    private sealed class DemoSftpSession : ISftpSession
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
                Closed?.Invoke(null);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class EchoSession(ConnectionProfile profile, int columns, int rows) : ITerminalSession
    {
        public event Action<byte[]>? DataReceived;

        public event Action<Exception?>? Closed;

        public bool IsOpen { get; private set; } = true;

        public void Start() => Emit(
            $"\u001b[1;36m{Banner}\u001b[0m ({columns}x{rows})\r\n" +
            "Couleurs : \u001b[31mrouge\u001b[0m \u001b[32mvert\u001b[0m \u001b[33mjaune\u001b[0m \u001b[34mbleu\u001b[0m — accents : é è à ç\r\n" +
            $"\u001b[32m{profile.Username}@{profile.Host}\u001b[0m:\u001b[34m~\u001b[0m$ ");

        public void Send(byte[] data) => Emit(Encoding.UTF8.GetString(data).Replace("\r", "\r\n"));

        public void Resize(int columns, int rows)
        {
        }

        public ValueTask DisposeAsync()
        {
            if (IsOpen)
            {
                IsOpen = false;
                Closed?.Invoke(null);
            }

            return ValueTask.CompletedTask;
        }

        private void Emit(string text) => DataReceived?.Invoke(Encoding.UTF8.GetBytes(text));
    }
}
