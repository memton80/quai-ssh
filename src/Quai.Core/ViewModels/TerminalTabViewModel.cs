using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Localization;
using Quai.Core.Models;
using Quai.Core.Serial;
using Quai.Core.Services;
using Quai.Core.Ssh;
using Quai.Core.Terminal;

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

/// <summary>
/// Un onglet de terminal : cycle de vie de la session (shell SSH ou port série) et échange d'octets avec l'émulateur.
/// </summary>
public sealed partial class TerminalTabViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ITerminalSessionFactory _factory;
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
        ITerminalSessionFactory factory,
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

    public ConnectionProfile Profile { get; private set; }

    public string Title => Profile.DisplayName;

    public string Endpoint => Profile.Endpoint;

    public bool IsSerial => Profile.IsSerial;

    /// <summary>Connexion rapide : l'onglet n'est relié à aucune fiche du carnet.</summary>
    public bool IsQuickConnect => Profile.IsTransient;

    /// <summary>Connexion rapide établie : proposer de l'ajouter au carnet (tant que l'utilisateur n'a pas refusé).</summary>
    public bool ShowSaveOffer => IsQuickConnect && IsConnected && !SaveOfferDismissed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSaveOffer))]
    public partial bool SaveOfferDismissed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(CanReconnect))]
    [NotifyPropertyChangedFor(nameof(CanSendBreak))]
    [NotifyPropertyChangedFor(nameof(ShowSaveOffer))]
    [NotifyCanExecuteChangedFor(nameof(SendBreakCommand))]
    public partial TerminalStatus Status { get; set; }

    /// <summary>
    /// La connexion rapide vient d'être ajoutée au carnet : l'onglet prend la fiche enregistrée (nom, réglages
    /// modifiés dans la fiche), utilisée à la prochaine reconnexion.
    /// </summary>
    public void AdoptSavedProfile(ConnectionProfile saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        Profile = saved.Clone();
        Profile.IsTransient = false;
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(IsQuickConnect));
        OnPropertyChanged(nameof(ShowSaveOffer));
    }

    /// <summary>Message affiché quand la session n'est pas (ou plus) connectée.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; }

    public bool IsConnected => Status == TerminalStatus.Connected;

    public bool IsConnecting => Status == TerminalStatus.Connecting;

    public bool CanReconnect => Status is TerminalStatus.Disconnected or TerminalStatus.Failed;

    /// <summary>Session connectée qui sait envoyer un « break » (liaison série).</summary>
    public bool CanSendBreak => IsConnected && _session is { CanSendBreak: true };

    /// <summary>Ouvre la session ; appelé par la vue dès que l'émulateur connaît sa taille.</summary>
    public async Task ConnectAsync(int columns, int rows)
    {
        if (_disposed || Status is TerminalStatus.Connecting or TerminalStatus.Connected)
        {
            return;
        }

        Remember(columns, rows);
        Status = TerminalStatus.Connecting;
        StatusText = _strings.Format(IsSerial ? "Terminal_SerialOpening" : "Terminal_Connecting", Endpoint);
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
            if (IsSerial)
            {
                // Un appareil série n'affiche souvent rien avant qu'on appuie sur Entrée.
                WriteNotice(_strings.Format("Terminal_SerialOpened", PortName));
            }

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
        catch (SerialPortUnavailableException ex)
        {
            var key = ex.Problem switch
            {
                SerialPortProblem.NotFound => "Terminal_SerialNotFound",
                SerialPortProblem.InUse => "Terminal_SerialInUse",
                _ => "Terminal_SerialSettingsRejected",
            };
            Fail(TerminalStatus.Failed, _strings.Format(key, ex.PortName));
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

    /// <summary>Signal « break » sur la liaison série (console de routeur, retour au moniteur de démarrage…).</summary>
    [RelayCommand(CanExecute = nameof(CanSendBreak))]
    public void SendBreak()
    {
        if (CanSendBreak)
        {
            _session?.SendBreak();
        }
    }

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

            Fail(TerminalStatus.Disconnected, ClosedMessage(error));
        });
    }

    private string PortName => Profile.Serial?.PortName ?? string.Empty;

    private string ClosedMessage(Exception? error) =>
        error is null ? _strings.Get("Terminal_Closed")
        : IsSerial ? _strings.Format("Terminal_SerialLost", PortName)
        : _strings.Format("Terminal_ConnectionLost", error.Message);

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
