namespace Quai.Core.Models;

/// <summary>
/// Une entrée du carnet de connexions. Ne contient volontairement aucun secret :
/// mots de passe et passphrases vivent dans le coffre Windows (<see cref="Security.ISecretStore"/>).
/// </summary>
public sealed class ConnectionProfile
{
    public const int DefaultPort = 22;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

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

    /// <summary>« utilisateur@hôte », suivi du port s'il n'est pas standard.</summary>
    public string Endpoint
    {
        get
        {
            var user = string.IsNullOrEmpty(Username) ? string.Empty : Username + "@";
            return Port == DefaultPort ? $"{user}{Host}" : $"{user}{Host}:{Port}";
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Endpoint : Name;

    public ConnectionProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
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
    };

    /// <summary>Nettoie les champs saisis (espaces, dossier, tags en double).</summary>
    public void Normalize()
    {
        Name = Name.Trim();
        Host = Host.Trim();
        Username = Username.Trim();
        PrivateKeyPath = string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath.Trim().Trim('"');
        Folder = FolderPath.Normalize(Folder);
        Tags = TagList.Normalize(Tags);
        Notes = Notes.Trim();
        if (string.IsNullOrEmpty(Name))
        {
            Name = Endpoint;
        }
    }
}
