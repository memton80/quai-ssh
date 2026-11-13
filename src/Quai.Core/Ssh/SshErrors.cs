namespace Quai.Core.Ssh;

/// <summary>La clé d'hôte n'a pas été acceptée : la connexion est abandonnée.</summary>
public sealed class HostKeyRejectedException : Exception
{
    public HostKeyRejectedException(HostKeyInfo presented, HostKeyStatus status)
        : base($"Clé d'hôte refusée pour {presented.Endpoint} ({status}).")
    {
        Presented = presented;
        Status = status;
    }

    public HostKeyInfo Presented { get; }

    public HostKeyStatus Status { get; }
}

/// <summary>Authentification refusée par le serveur après plusieurs tentatives.</summary>
public sealed class SshLoginFailedException : Exception
{
    public SshLoginFailedException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>Le fichier de clé privée est introuvable ou illisible.</summary>
public sealed class PrivateKeyUnavailableException : Exception
{
    public PrivateKeyUnavailableException(string path, Exception? inner = null)
        : base($"Clé privée inutilisable : {path}", inner)
    {
        Path = path;
    }

    public string Path { get; }
}
