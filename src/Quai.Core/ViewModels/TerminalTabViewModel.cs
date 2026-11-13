using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Localization;
using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Ssh;

namespace Quai.Core.ViewModels;

public enum TerminalStatus
{
    /// <summary>Onglet créé, en attente de la taille du terminal.</summary>
    Idle,
    Connecting,
    Connected,
    Disconnected,
    Failed,
}

/// <summary>Un onglet de terminal : cycle de vie de la session SSH et échange d'octets avec l'émulateur.</summary>
public sealed partial class TerminalTabViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ISshSessionFactory _factory;
    private readonly IStrings _strings;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger _logger;
    private ITerminalSession? _session;
    private CancellationTokenSource? _connectCancellation;
    private int _columns = 80;
    private int _rows = 24;
    private bool _disposed;

    public TerminalTabViewModel(
        ConnectionProfile profile,
        ISshSessionFactory factory,
        IStrings strings,
        IUiDispatcher dispatcher,
        ILogger? logger = null)
    {
        Profile = profile;
        _factory = factory;
        _strings = strings;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger.Instance;
        Status = TerminalStatus.Idle;
        StatusText = string.Empty;
    }

    /// <summary>Octets à écrire dans l'émulateur (peut être déclenché depuis n'importe quel fil).</summary>
    public event Action<byte[]>? Output;

    public ConnectionProfile Profile { get; }

    public string Title => Profile.DisplayName;

    public string Endpoint => Profile.Endpoint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(CanReconnect))]
    public partial TerminalStatus Status { get; set; }

    /// <summary>Message affiché quand la session n'est pas (ou plus) connectée.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; }

    public bool IsConnected => Status == TerminalStatus.Connected;

    public bool IsConnecting => Status == TerminalStatus.Connecting;

    public bool CanReconnect => Status is TerminalStatus.Disconnected or TerminalStatus.Failed;

    /// <summary>Ouvre la session ; appelé par la vue dès que l'émulateur connaît sa taille.</summary>
    public async Task ConnectAsync(int columns, int rows)
    {
        if (_disposed || Status is TerminalStatus.Connecting or TerminalStatus.Connected)
        {
            return;
        }

        Remember(columns, rows);
        Status = TerminalStatus.Connecting;
        StatusText = _strings.Format("Terminal_Connecting", Endpoint);
        WriteNotice(StatusText);

        var cancellation = new CancellationTokenSource();
        _connectCancellation = cancellation;
        try
        {
            var session = await _factory.OpenTerminalAsync(Profile, _columns, _rows, cancellation.Token);
            if (_disposed)
            {
                await session.DisposeAsync();
                return;
            }

            _session = session;
            session.DataReceived += OnDataReceived;
            session.Closed += OnSessionClosed;
            Status = TerminalStatus.Connected;
            StatusText = string.Empty;
            session.Start();
            session.Resize(_columns, _rows);
        }
        catch (OperationCanceledException)
        {
            Fail(TerminalStatus.Disconnected, _strings.Get("Terminal_Cancelled"));
        }
        catch (HostKeyRejectedException)
        {
            Fail(TerminalStatus.Failed, _strings.Get("Terminal_HostKeyRejected"));
        }
        catch (SshLoginFailedException)
        {
            Fail(TerminalStatus.Failed, _strings.Get("Terminal_AuthenticationFailed"));
        }
        catch (PrivateKeyUnavailableException ex)
        {
            Fail(TerminalStatus.Failed, _strings.Format("Terminal_KeyUnavailable", ex.Path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Ouverture du terminal vers {Endpoint} impossible ({Error})", Endpoint, ex.GetType().Name);
            Fail(TerminalStatus.Failed, _strings.Format("Terminal_ConnectFailed", ex.Message));
        }
        finally
        {
            _connectCancellation = null;
            cancellation.Dispose();
        }
    }

    [RelayCommand]
    public Task ReconnectAsync() => CanReconnect ? ConnectAsync(_columns, _rows) : Task.CompletedTask;

    /// <summary>Texte saisi dans l'émulateur (UTF-8 vers le serveur).</summary>
    public void SendInput(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _session?.Send(Encoding.UTF8.GetBytes(text));
        }
    }

    /// <summary>Données binaires de l'émulateur (rapports de souris) : un caractère = un octet.</summary>
    public void SendBinary(string? data)
    {
        if (!string.IsNullOrEmpty(data))
        {
            _session?.Send(Encoding.Latin1.GetBytes(data));
        }
    }

    public void Resize(int columns, int rows)
    {
        Remember(columns, rows);
        _session?.Resize(_columns, _rows);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connectCancellation?.Cancel();
        var session = Detach();
        if (session is not null)
        {
            await session.DisposeAsync();
        }
    }

    private void Remember(int columns, int rows)
    {
        if (columns > 0 && rows > 0)
        {
            _columns = columns;
            _rows = rows;
        }
    }

    private ITerminalSession? Detach()
    {
        var session = _session;
        _session = null;
        if (session is not null)
        {
            session.DataReceived -= OnDataReceived;
            session.Closed -= OnSessionClosed;
        }

        return session;
    }

    private void OnDataReceived(byte[] data) => Output?.Invoke(data);

    private void OnSessionClosed(Exception? error)
    {
        // Notifié depuis le fil de lecture SSH : on repasse sur le fil d'interface.
        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            var session = Detach();
            if (session is not null)
            {
                _ = session.DisposeAsync().AsTask();
            }

            Fail(
                TerminalStatus.Disconnected,
                error is null ? _strings.Get("Terminal_Closed") : _strings.Format("Terminal_ConnectionLost", error.Message));
        });
    }

    private void Fail(TerminalStatus status, string message)
    {
        Status = status;
        StatusText = message;
        WriteNotice(message);
    }

    /// <summary>Message de l'application dans le terminal (gris, sur sa propre ligne).</summary>
    private void WriteNotice(string message) =>
        Output?.Invoke(Encoding.UTF8.GetBytes($"\r\n\u001b[90m{message}\u001b[0m\r\n"));
}
