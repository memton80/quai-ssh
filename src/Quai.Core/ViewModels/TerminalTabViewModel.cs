using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Local;
using Quai.Core.Localization;
using Quai.Core.Models;
using Quai.Core.Serial;
using Quai.Core.Services;
using Quai.Core.Ssh;
using Quai.Core.Storage;
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
/// Un onglet de terminal : cycle de vie de la session (shell SSH, port série ou shell local) et échange d'octets avec
/// l'émulateur.
/// </summary>
public sealed partial class TerminalTabViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>
    /// Attente avant chaque tentative de reconnexion automatique (environ deux minutes en tout) : de quoi laisser
    /// le réseau revenir après une sortie de veille ou un changement de Wi-Fi, sans insister indéfiniment.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> ReconnectDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    ];

    private readonly ITerminalSessionFactory _factory;
    private readonly IStrings _strings;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly ISettingsService? _settings;
    private readonly TimeProvider _time;
    private ITerminalSession? _session;
    private CancellationTokenSource? _connectCancellation;
    private CancellationTokenSource? _autoReconnect;

    // Dernière cause de déconnexion, reprise dans le compte à rebours.
    private string _lastFailure = string.Empty;

    // Dossier courant annoncé par le shell (OSC 7) ; alimenté depuis le fil de lecture de la session.
    private readonly WorkingDirectoryTracker _directory = new();
    private int _columns = 80;
    private int _rows = 24;
    private bool _disposed;

    /// <param name="settings">Réglages (reconnexion automatique) ; sans réglages, pas de reconnexion automatique.</param>
    /// <param name="time">Horloge des tentatives de reconnexion (remplacée dans les tests).</param>
    public TerminalTabViewModel(
        ConnectionProfile profile,
        ITerminalSessionFactory factory,
        IStrings strings,
        IUiDispatcher dispatcher,
        ILogger? logger = null,
        ISettingsService? settings = null,
        TimeProvider? time = null)
    {
        Profile = profile;
        _factory = factory;
        _strings = strings;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger.Instance;
        _settings = settings;
        _time = time ?? TimeProvider.System;
        Status = TerminalStatus.Idle;
        StatusText = string.Empty;
    }

    private enum ConnectOutcome
    {
        Connected,

        /// <summary>Échec qui peut venir du réseau : une nouvelle tentative a un sens.</summary>
        Retryable,

        /// <summary>Refus (authentification, clé d'hôte), saisie annulée, onglet fermé : on n'insiste pas.</summary>
        Final,
    }

    /// <summary>Octets à écrire dans l'émulateur (peut être déclenché depuis n'importe quel fil).</summary>
    public event Action<byte[]>? Output;

    public ConnectionProfile Profile { get; private set; }

    public string Title => Profile.DisplayName;

    public string Endpoint => Profile.Endpoint;

    public bool IsSerial => Profile.IsSerial;

    /// <summary>Shell de cet ordinateur (pseudo-console), et non une connexion.</summary>
    public bool IsLocal => Profile.IsLocal;

    /// <summary>Session SSH : explorateur SFTP « ici », reconnexion automatique, ajout au carnet.</summary>
    public bool IsSsh => Profile.IsSsh;

    /// <summary>Texte du bouton du bandeau « session fermée » : on relance un shell local, on se reconnecte ailleurs.</summary>
    public string ReconnectText => _strings.Get(IsLocal ? "Terminal_Restart" : "Terminal_Reconnect");

    /// <summary>Connexion rapide : l'onglet n'est relié à aucune fiche du carnet.</summary>
    public bool IsQuickConnect => Profile.IsTransient && IsSsh;

    /// <summary>Connexion rapide établie : proposer de l'ajouter au carnet (tant que l'utilisateur n'a pas refusé).</summary>
    public bool ShowSaveOffer => IsQuickConnect && IsConnected && !SaveOfferDismissed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSaveOffer))]
    public partial bool SaveOfferDismissed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(CanReconnect))]
    [NotifyPropertyChangedFor(nameof(ShowReconnectBar))]
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

    /// <summary>Connexion perdue : des tentatives automatiques sont programmées (compte à rebours dans <see cref="StatusText"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReconnectBar))]
    [NotifyCanExecuteChangedFor(nameof(CancelAutoReconnectCommand))]
    public partial bool IsAutoReconnecting { get; set; }

    /// <summary>Bandeau « déconnecté » : session fermée, ou tentatives automatiques en cours (y compris pendant une tentative).</summary>
    public bool ShowReconnectBar => CanReconnect || IsAutoReconnecting;

    public bool IsConnected => Status == TerminalStatus.Connected;

    public bool IsConnecting => Status == TerminalStatus.Connecting;

    public bool CanReconnect => Status is TerminalStatus.Disconnected or TerminalStatus.Failed;

    /// <summary>Session connectée qui sait envoyer un « break » (liaison série).</summary>
    public bool CanSendBreak => IsConnected && _session is { CanSendBreak: true };

    /// <summary>Ouvre la session ; appelé par la vue dès que l'émulateur connaît sa taille.</summary>
    public async Task ConnectAsync(int columns, int rows)
    {
        Remember(columns, rows);
        await ConnectCoreAsync();
    }

    /// <summary>« Reconnecter » : tentative immédiate (les tentatives automatiques programmées sont abandonnées).</summary>
    [RelayCommand]
    public async Task ReconnectAsync()
    {
        if (CanReconnect)
        {
            StopAutoReconnect();
            await ConnectCoreAsync();
        }
    }

    /// <summary>Arrête les tentatives de reconnexion automatique ; l'onglet reste déconnecté.</summary>
    [RelayCommand(CanExecute = nameof(IsAutoReconnecting))]
    public void CancelAutoReconnect()
    {
        if (IsAutoReconnecting)
        {
            StopAutoReconnect();
            StatusText = _strings.Format("Terminal_ReconnectStopped", _lastFailure);
        }
    }

    private async Task<ConnectOutcome> ConnectCoreAsync()
    {
        if (_disposed || Status is TerminalStatus.Connecting or TerminalStatus.Connected)
        {
            return ConnectOutcome.Final;
        }

        var restarting = Status != TerminalStatus.Idle;
        Status = TerminalStatus.Connecting;
        StatusText = Profile.Protocol switch
        {
            ConnectionProtocol.Serial => _strings.Format("Terminal_SerialOpening", Endpoint),
            ConnectionProtocol.Local => _strings.Format("Terminal_LocalStarting", Title),
            _ => _strings.Format("Terminal_Connecting", Endpoint),
        };

        // Un shell local s'ouvre sans délai : pas de message avant sa première invite, sauf pour séparer une relance.
        if (!IsLocal || restarting)
        {
            WriteNotice(StatusText);
        }

        var cancellation = new CancellationTokenSource();
        _connectCancellation = cancellation;
        try
        {
            var session = await _factory.OpenTerminalAsync(Profile, _columns, _rows, cancellation.Token);
            if (_disposed)
            {
                await session.DisposeAsync();
                return ConnectOutcome.Final;
            }

            _directory.Reset();
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
            return ConnectOutcome.Connected;
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
        catch (LocalShellUnavailableException ex)
        {
            Fail(TerminalStatus.Failed, _strings.Format("Terminal_LocalUnavailable", ex.ShellName, ex.Message));
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
            return ConnectOutcome.Retryable;
        }
        finally
        {
            _connectCancellation = null;
            cancellation.Dispose();
        }

        return ConnectOutcome.Final;
    }

    private bool AutoReconnectEnabled => IsSsh && _settings?.Current.AutoReconnect == true;

    /// <summary>Connexion perdue : tentatives espacées (<see cref="ReconnectDelays"/>), compte à rebours affiché.</summary>
    private void StartAutoReconnect()
    {
        StopAutoReconnect();
        var cancellation = new CancellationTokenSource();
        _autoReconnect = cancellation;
        IsAutoReconnecting = true;
        _ = AutoReconnectAsync(cancellation);
    }

    private void StopAutoReconnect()
    {
        var cancellation = _autoReconnect;
        _autoReconnect = null;
        IsAutoReconnecting = false;
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private async Task AutoReconnectAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            for (var attempt = 1; attempt <= ReconnectDelays.Count; attempt++)
            {
                for (var remaining = (int)Math.Ceiling(ReconnectDelays[attempt - 1].TotalSeconds); remaining > 0; remaining--)
                {
                    StatusText = _strings.Format("Terminal_ReconnectIn", _lastFailure, remaining, attempt, ReconnectDelays.Count);
                    await Task.Delay(TimeSpan.FromSeconds(1), _time, token);
                }

                if (token.IsCancellationRequested || _disposed || !CanReconnect)
                {
                    return;
                }

                _logger.LogInformation("Reconnexion automatique à {Endpoint} (tentative {Attempt})", Endpoint, attempt);
                var outcome = await ConnectCoreAsync();
                if (outcome != ConnectOutcome.Retryable || token.IsCancellationRequested)
                {
                    // Reconnecté, ou refus qui ne se réglera pas tout seul (mot de passe, clé d'hôte, saisie annulée).
                    return;
                }

                _lastFailure = StatusText;
            }

            StatusText = _strings.Format("Terminal_ReconnectGaveUp", _lastFailure, ReconnectDelays.Count);
        }
        catch (OperationCanceledException)
        {
            // Arrêté : « Annuler », « Reconnecter » ou fermeture de l'onglet.
        }
        finally
        {
            if (ReferenceEquals(_autoReconnect, cancellation))
            {
                _autoReconnect = null;
                IsAutoReconnecting = false;
                cancellation.Dispose();
            }
        }
    }

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
        StopAutoReconnect();
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

    private void OnDataReceived(byte[] data)
    {
        _directory.Feed(data);
        Output?.Invoke(data);
    }

    /// <summary>
    /// Dossier courant du shell distant, pour ouvrir l'explorateur SFTP au même endroit : celui que le shell annonce
    /// (OSC 7), sinon celui que la session sait lire (SSH), sinon <c>null</c> (dossier personnel).
    /// </summary>
    public async Task<string?> GetWorkingDirectoryAsync(CancellationToken cancellationToken = default)
    {
        if (_session is not { } session || !IsConnected || !IsSsh)
        {
            return null;
        }

        if (_directory.Current is { } announced)
        {
            return announced;
        }

        try
        {
            return await session.QueryWorkingDirectoryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogInformation("Dossier courant du shell illisible ({Error})", ex.GetType().Name);
            return null;
        }
    }

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

            // Coupure (réseau, veille) et non fin normale de la session (« exit ») : on retente tout seul.
            if (error is not null && AutoReconnectEnabled)
            {
                _lastFailure = StatusText;
                StartAutoReconnect();
            }
        });
    }

    private string PortName => Profile.Serial?.PortName ?? string.Empty;

    private string ClosedMessage(Exception? error) => error switch
    {
        null when IsLocal => _strings.Format("Terminal_LocalExited", Title),
        null => _strings.Get("Terminal_Closed"),
        LocalShellExitedException exited => _strings.Format("Terminal_LocalExitedWithCode", Title, exited.ExitCode),
        _ when IsLocal => _strings.Format("Terminal_LocalLost", Title, error.Message),
        _ when IsSerial => _strings.Format("Terminal_SerialLost", PortName),
        _ => _strings.Format("Terminal_ConnectionLost", error.Message),
    };

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
