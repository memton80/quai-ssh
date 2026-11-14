using System.Globalization;
using System.Text.Json.Serialization;

namespace Quai.Core.Models;

public enum SerialParity
{
    None,
    Odd,
    Even,
    Mark,
    Space,
}

public enum SerialStopBits
{
    One,
    OnePointFive,
    Two,
}

public enum SerialFlowControl
{
    None,

    /// <summary>Contrôle de flux logiciel (caractères XON / XOFF).</summary>
    XonXoff,

    /// <summary>Contrôle de flux matériel (lignes RTS / CTS).</summary>
    RtsCts,
}

/// <summary>
/// Paramètres d'une liaison série. Valeurs par défaut de PuTTY : 9600 bauds, 8 bits de données,
/// sans parité, 1 bit d'arrêt (« 8N1 »), sans contrôle de flux.
/// </summary>
public sealed class SerialSettings
{
    public const int DefaultBaudRate = 9600;

    public const int DefaultDataBits = 8;

    public const int MinDataBits = 5;

    public const int MaxDataBits = 8;

    /// <summary>Plafond de saisie (les adaptateurs USB les plus rapides montent à 12 Mbauds).</summary>
    public const int MaxBaudRate = 12_000_000;

    /// <summary>Vitesses proposées dans la fiche de connexion (toute autre valeur reste saisissable).</summary>
    public static readonly IReadOnlyList<int> StandardBaudRates =
        [300, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200, 230400, 460800, 921600];

    /// <summary>Nom du port, ex. <c>COM3</c>.</summary>
    public string PortName { get; set; } = string.Empty;

    public int BaudRate { get; set; } = DefaultBaudRate;

    public int DataBits { get; set; } = DefaultDataBits;

    public SerialParity Parity { get; set; } = SerialParity.None;

    public SerialStopBits StopBits { get; set; } = SerialStopBits.One;

    public SerialFlowControl FlowControl { get; set; } = SerialFlowControl.None;

    /// <summary>Notation usuelle du format des caractères : bits de données, parité, bits d'arrêt (« 8N1 »).</summary>
    [JsonIgnore]
    public string Frame
    {
        get
        {
            var parity = Parity switch
            {
                SerialParity.Odd => 'O',
                SerialParity.Even => 'E',
                SerialParity.Mark => 'M',
                SerialParity.Space => 'S',
                _ => 'N',
            };
            var stopBits = StopBits switch
            {
                SerialStopBits.OnePointFive => "1.5",
                SerialStopBits.Two => "2",
                _ => "1",
            };
            return string.Create(CultureInfo.InvariantCulture, $"{DataBits}{parity}{stopBits}");
        }
    }

    /// <summary>« COM3 · 115200 8N1 », suivi du contrôle de flux s'il y en a un.</summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            var summary = string.Create(CultureInfo.InvariantCulture, $"{PortName} · {BaudRate} {Frame}");
            return FlowControl switch
            {
                SerialFlowControl.XonXoff => summary + " · XON/XOFF",
                SerialFlowControl.RtsCts => summary + " · RTS/CTS",
                _ => summary,
            };
        }
    }

    public SerialSettings Clone() => new()
    {
        PortName = PortName,
        BaudRate = BaudRate,
        DataBits = DataBits,
        Parity = Parity,
        StopBits = StopBits,
        FlowControl = FlowControl,
    };

    /// <summary>Nom du port en majuscules, valeurs hors limites ramenées aux valeurs par défaut.</summary>
    public void Normalize()
    {
        PortName = SerialPortName.Normalize(PortName);
        if (BaudRate is < 1 or > MaxBaudRate)
        {
            BaudRate = DefaultBaudRate;
        }

        if (DataBits is < MinDataBits or > MaxDataBits)
        {
            DataBits = DefaultDataBits;
        }

        if (!Enum.IsDefined(Parity))
        {
            Parity = SerialParity.None;
        }

        if (!Enum.IsDefined(StopBits))
        {
            StopBits = SerialStopBits.One;
        }

        if (!Enum.IsDefined(FlowControl))
        {
            FlowControl = SerialFlowControl.None;
        }
    }
}
