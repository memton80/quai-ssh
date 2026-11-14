using Quai.Core.Models;
using Quai.Core.Serial;
using Quai.Core.Services;
using Quai.Core.Sftp;
using Quai.Core.Ssh;
using Quai.Core.Terminal;

namespace Quai.Core.Tests.Support;

/// <summary>Réponses scriptées aux questions posées pendant une connexion.</summary>
public sealed class ScriptedPrompts : IHostKeyPrompt, ICredentialPrompt
{
    public bool AcceptUnknownHost { get; set; } = true;

    public bool AcceptChangedHost { get; set; }

    /// <summary>Secrets fournis à chaque demande, dans l'ordre ; <c>null</c> = annulation.</summary>
    public Queue<SecretInput?> Secrets { get; } = new();

    public List<HostKeyInfo> UnknownHostRequests { get; } = [];

    public List<(HostKeyInfo Presented, IReadOnlyList<HostKeyInfo> Known)> ChangedHostRequests { get; } = [];

    public List<(SecretKind Kind, bool IsRetry)> SecretRequests { get; } = [];

    public Task<bool> ConfirmUnknownHostAsync(HostKeyInfo presented)
    {
        UnknownHostRequests.Add(presented);
        return Task.FromResult(AcceptUnknownHost);
    }

    public Task<bool> ConfirmChangedHostAsync(HostKeyInfo presented, IReadOnlyList<HostKeyInfo> known)
    {
        ChangedHostRequests.Add((presented, known));
        return Task.FromResult(AcceptChangedHost);
    }

    public Task<SecretInput?> PromptSecretAsync(ConnectionProfile profile, SecretKind kind, bool isRetry)
    {
        SecretRequests.Add((kind, isRetry));
        return Task.FromResult(Secrets.Count > 0 ? Secrets.Dequeue() : null);
    }
}

/// <summary>Exécute immédiatement (pas de fil d'interface dans les tests).</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

/// <summary>Session de terminal factice pilotée par le test.</summary>
public sealed class FakeTerminalSession : ITerminalSession
{
    public event Action<byte[]>? DataReceived;

    public event Action<Exception?>? Closed;

    public bool IsOpen { get; private set; } = true;

    public bool CanSendBreak { get; init; }

    public int Breaks { get; private set; }

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    public List<byte[]> Sent { get; } = [];

    public List<(int Columns, int Rows)> Sizes { get; } = [];

    public void Start() => Started = true;

    public void Send(byte[] data) => Sent.Add(data);

    public void Resize(int columns, int rows) => Sizes.Add((columns, rows));

    public void SendBreak() => Breaks++;

    public void Emit(string text) => DataReceived?.Invoke(System.Text.Encoding.UTF8.GetBytes(text));

    public void Close(Exception? error = null)
    {
        IsOpen = false;
        Closed?.Invoke(error);
    }

    /// <summary>Si renseignée, la fermeture attend ce signal (serveur qui ne répond plus).</summary>
    public TaskCompletionSource? DisposeGate { get; set; }

    public async ValueTask DisposeAsync()
    {
        Disposed = true;
        IsOpen = false;
        if (DisposeGate is { } gate)
        {
            await gate.Task;
        }
    }
}

/// <summary>Fabrique de sessions factice : retourne les sessions préparées ou lève l'erreur demandée.</summary>
public sealed class FakeSessionFactory : ISshSessionFactory
{
    public Queue<Func<ITerminalSession>> Terminals { get; } = new();

    public Queue<Func<ISftpSession>> SftpSessions { get; } = new();

    public List<(ConnectionProfile Profile, int Columns, int Rows)> TerminalRequests { get; } = [];

    public List<ConnectionProfile> SftpRequests { get; } = [];

    public Task<ITerminalSession> OpenTerminalAsync(
        ConnectionProfile profile,
        int columns,
        int rows,
        CancellationToken cancellationToken = default)
    {
        TerminalRequests.Add((profile, columns, rows));
        try
        {
            return Task.FromResult(Terminals.Dequeue()());
        }
        catch (Exception ex)
        {
            return Task.FromException<ITerminalSession>(ex);
        }
    }

    public Task<ISftpSession> OpenSftpAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        SftpRequests.Add(profile);
        try
        {
            return Task.FromResult(SftpSessions.Dequeue()());
        }
        catch (Exception ex)
        {
            return Task.FromException<ISftpSession>(ex);
        }
    }
}

/// <summary>Fabrique de sessions série factice.</summary>
public sealed class FakeSerialSessionFactory : ISerialSessionFactory
{
    public Queue<Func<ITerminalSession>> Sessions { get; } = new();

    public List<SerialSettings> Requests { get; } = [];

    public Task<ITerminalSession> OpenAsync(SerialSettings settings, CancellationToken cancellationToken = default)
    {
        Requests.Add(settings);
        try
        {
            return Task.FromResult(Sessions.Dequeue()());
        }
        catch (Exception ex)
        {
            return Task.FromException<ITerminalSession>(ex);
        }
    }
}

/// <summary>Ports série « présents », modifiables par le test.</summary>
public sealed class FakeSerialPortCatalog : ISerialPortCatalog
{
    public List<string> Ports { get; } = [];

    public int Reads { get; private set; }

    public IReadOnlyList<string> GetPortNames()
    {
        Reads++;
        return [.. Ports];
    }
}

public sealed class RecordingNavigator : IShellNavigator
{
    public List<AppPage> Pages { get; } = [];

    public void NavigateTo(AppPage page) => Pages.Add(page);
}
