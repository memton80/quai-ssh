using System.Text.Json.Serialization;

namespace Quai.Core.Models;

/// <summary>
/// Une entrée du carnet de connexions : serveur SSH ou port série (ou, hors carnet, l'onglet d'un terminal local). Ne contient volontairement aucun secret :
/// mots de passe et passphrases vivent dans le coffre Windows (<see cref="Security.ISecretStore"/>).
/// </summary>
public sealed class ConnectionProfile
{
    public const int DefaultPort = 22;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>SSH (par défaut, y compris pour les carnets enregistrés avant l'arrivée du série) ou port série.</summary>
    public ConnectionProtocol Protocol { get; set; } = ConnectionProtocol.Ssh;

    /// <summary>Paramètres de la liaison ; renseignés pour une connexion série uniquement.</summary>
    public SerialSettings? Serial { get; set; }

    /// <summary>
    /// Shell lancé par un terminal local. Jamais enregistré : un terminal local n'entre pas dans le carnet
    /// (le format du fichier reste celui des versions précédentes).
    /// </summary>
    [JsonIgnore]
    public LocalShell? LocalShell { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = DefaultPort;

    public string Username { get; set; } = string.Empty;

    public AuthenticationKind Authentication { get; set; } = AuthenticationKind.Password;

    /// <summary>Chemin du fichier de clé privée (le fichier lui-même n'est jamais copié).</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>Dossier de rangement, niveaux séparés par « / » (vide = racine).</summary>
    public string Folder { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastConnectedAt { get; set; }

    [JsonIgnore]
    public bool IsSerial => Protocol == ConnectionProtocol.Serial;

    [JsonIgnore]
    public bool IsLocal => Protocol == ConnectionProtocol.Local;

    /// <summary>Serveur SSH : seul type qui a un explorateur SFTP, une reconnexion automatique, une place dans le carnet.</summary>
    [JsonIgnore]
    public bool IsSsh => Protocol == ConnectionProtocol.Ssh;

    /// <summary>
    /// Connexion rapide, absente du carnet : jamais enregistrée telle quelle, et aucun secret n'est mémorisé
    /// dans le coffre pour elle (il resterait orphelin si elle n'est pas ajoutée au carnet).
    /// </summary>
    [JsonIgnore]
    public bool IsTransient { get; set; }

    /// <summary>
    /// « utilisateur@hôte », suivi du port s'il n'est pas standard ; pour une connexion série,
    /// « COM3 · 9600 8N1 » (voir <see cref="SerialSettings.Summary"/>) ; pour un terminal local, la ligne de commande du shell.
    /// </summary>
    public string Endpoint
    {
        get
        {
            if (IsLocal)
            {
                return LocalShell?.CommandLine ?? string.Empty;
            }

            if (IsSerial)
            {
                return (Serial ?? new SerialSettings()).Summary;
            }

            var user = string.IsNullOrEmpty(Username) ? string.Empty : Username + "@";
            return Port == DefaultPort ? $"{user}{Host}" : $"{user}{Host}:{Port}";
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name;

    /// <summary>Nom par défaut : l'adresse SSH, le seul nom du port (sa vitesse peut changer), ou le nom du shell.</summary>
    private string DefaultName => Protocol switch
    {
        ConnectionProtocol.Serial => Serial?.PortName ?? string.Empty,
        ConnectionProtocol.Local => LocalShell?.Name ?? string.Empty,
        _ => Endpoint,
    };

    /// <summary>Onglet de terminal local : hors carnet, jamais enregistré.</summary>
    public static ConnectionProfile ForLocalShell(LocalShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        return new ConnectionProfile
        {
            Name = shell.Name,
            Protocol = ConnectionProtocol.Local,
            LocalShell = shell,
            IsTransient = true,
        };
    }

    public ConnectionProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Protocol = Protocol,
        Serial = Serial?.Clone(),
        LocalShell = LocalShell,
        Host = Host,
        Port = Port,
        Username = Username,
        Authentication = Authentication,
        PrivateKeyPath = PrivateKeyPath,
        Folder = Folder,
        Tags = [.. Tags],
        Notes = Notes,
        CreatedAt = CreatedAt,
        LastConnectedAt = LastConnectedAt,
        IsTransient = IsTransient,
    };

    /// <summary>
    /// Nettoie les champs saisis (espaces, dossier, tags en double). Une connexion série ou locale ne garde aucun
    /// champ SSH, une connexion SSH aucun paramètre série.
    /// </summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(Protocol))
        {
            Protocol = ConnectionProtocol.Ssh;
        }

        Name = Name.Trim();
        if (!IsSerial)
        {
            Serial = null;
        }

        if (!IsLocal)
        {
            LocalShell = null;
        }

        if (IsSerial || IsLocal)
        {
            if (IsSerial)
            {
                Serial ??= new SerialSettings();
                Serial.Normalize();
            }

            Host = string.Empty;
            Port = DefaultPort;
            Username = string.Empty;
            Authentication = AuthenticationKind.Password;
            PrivateKeyPath = null;
        }
        else
        {
            Host = Host.Trim();
            Username = Username.Trim();
            PrivateKeyPath = string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath.Trim().Trim('"');
        }

        Folder = FolderPath.Normalize(Folder);
        Tags = TagList.Normalize(Tags);
        Notes = Notes.Trim();
        if (string.IsNullOrEmpty(Name))
        {
            Name = DefaultName;
        }
    }
}
