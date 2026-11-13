using System.Text;
using Quai.Core.Models;
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
