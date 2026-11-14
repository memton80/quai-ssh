using System.IO.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Models;
using Quai.Core.Terminal;

namespace Quai.Core.Serial;

/// <summary>Ouvre un terminal sur un port série.</summary>
public interface ISerialSessionFactory
{
    Task<ITerminalSession> OpenAsync(SerialSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>Ports série de cet ordinateur.</summary>
public interface ISerialPortCatalog
{
    /// <summary>Noms des ports présents (COM1, COM3…), triés par numéro.</summary>
    IReadOnlyList<string> GetPortNames();
}

/// <summary>Liste des ports tenue par Windows (<see cref="SerialPort.GetPortNames"/>).</summary>
public sealed class SerialPortCatalog : ISerialPortCatalog
{
    public IReadOnlyList<string> GetPortNames()
    {
        try
        {
            return SerialPortName.Sort(SerialPort.GetPortNames());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or PlatformNotSupportedException)
        {
            return [];
        }
    }
}

/// <summary>Terminal série au-dessus de <see cref="SerialPort"/> (System.IO.Ports, paquet officiel de .NET).</summary>
public sealed class SerialSessionFactory : ISerialSessionFactory
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorInvalidParameter = unchecked((int)0x80070057);

    private readonly ILogger _logger;

    public SerialSessionFactory(ILogger<SerialSessionFactory>? logger = null)
    {
        _logger = logger ?? NullLogger<SerialSessionFactory>.Instance;
    }

    public Task<ITerminalSession> OpenAsync(SerialSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var copy = settings.Clone();

        // L'ouverture d'un port (pilote USB qui se réveille) peut prendre un moment : hors du fil d'interface.
        return Task.Run<ITerminalSession>(() => Open(copy), cancellationToken);
    }

    /// <summary>Erreur d'ouverture de System.IO.Ports traduite en cause compréhensible.</summary>
    internal static Exception Translate(string portName, Exception error) => error switch
    {
        FileNotFoundException or DirectoryNotFoundException =>
            new SerialPortUnavailableException(portName, SerialPortProblem.NotFound, error),
        UnauthorizedAccessException =>
            new SerialPortUnavailableException(portName, SerialPortProblem.InUse, error),
        IOException { HResult: ErrorSharingViolation } =>
            new SerialPortUnavailableException(portName, SerialPortProblem.InUse, error),
        ArgumentException or IOException { HResult: ErrorInvalidParameter } =>
            new SerialPortUnavailableException(portName, SerialPortProblem.SettingsRejected, error),
        _ => error,
    };

    internal SerialTerminalSession Open(SerialSettings settings)
    {
        SerialPort? port = null;
        try
        {
            port = new SerialPort(
                settings.PortName,
                settings.BaudRate,
                ToParity(settings.Parity),
                settings.DataBits,
                ToStopBits(settings.StopBits))
            {
                Handshake = ToHandshake(settings.FlowControl),
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = SerialPort.InfiniteTimeout,
            };
            port.Open();
        }
        catch (Exception ex)
        {
            port?.Dispose();
            _logger.LogWarning("Ouverture du port série {Port} impossible ({Error})", settings.PortName, ex.GetType().Name);
            throw Translate(settings.PortName, ex);
        }

        // Comme PuTTY : DTR et RTS levés tant que le port est ouvert (RTS reste au contrôle de flux matériel
        // s'il est choisi). Certains appareils (USB CDC, consoles) n'émettent rien tant que DTR est bas.
        SetControlLine(port, "DTR", p => p.DtrEnable = true);
        if (settings.FlowControl != SerialFlowControl.RtsCts)
        {
            SetControlLine(port, "RTS", p => p.RtsEnable = true);
        }

        _logger.LogInformation("Port série ouvert : {Summary}", settings.Summary);
        return new SerialTerminalSession(new SystemSerialLine(port));
    }

    private void SetControlLine(SerialPort port, string line, Action<SerialPort> set)
    {
        try
        {
            set(port);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Ports virtuels sans lignes de modem (pty, certains pilotes Bluetooth) : la liaison fonctionne sans.
            _logger.LogInformation("Ligne {Line} non pilotable sur {Port} ({Error})", line, port.PortName, ex.GetType().Name);
        }
    }

    private static Parity ToParity(SerialParity parity) => parity switch
    {
        SerialParity.Odd => Parity.Odd,
        SerialParity.Even => Parity.Even,
        SerialParity.Mark => Parity.Mark,
        SerialParity.Space => Parity.Space,
        _ => Parity.None,
    };

    private static StopBits ToStopBits(SerialStopBits stopBits) => stopBits switch
    {
        SerialStopBits.OnePointFive => StopBits.OnePointFive,
        SerialStopBits.Two => StopBits.Two,
        _ => StopBits.One,
    };

    private static Handshake ToHandshake(SerialFlowControl flowControl) => flowControl switch
    {
        SerialFlowControl.XonXoff => Handshake.XOnXOff,
        SerialFlowControl.RtsCts => Handshake.RequestToSend,
        _ => Handshake.None,
    };

    /// <summary>Le flux est lu une fois pour toutes : <see cref="SerialPort.BaseStream"/> lève dès le port fermé.</summary>
    private sealed class SystemSerialLine(SerialPort port) : ISerialLine
    {
        public Stream Stream { get; } = port.BaseStream;

        public void SetBreak(bool on) => port.BreakState = on;

        public void Dispose()
        {
            // Ce qui n'est pas encore parti est abandonné : sinon la fermeture attend que l'appareil ait tout reçu,
            // indéfiniment si le contrôle de flux le bloque, et le port resterait pris.
            try
            {
                if (port.IsOpen)
                {
                    port.DiscardOutBuffer();
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                // Port déjà perdu (débranché) : la fermeture fera au mieux.
            }

            port.Dispose();
        }
    }
}
