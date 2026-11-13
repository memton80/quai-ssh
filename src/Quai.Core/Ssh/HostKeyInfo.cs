using System.Security.Cryptography;

namespace Quai.Core.Ssh;

/// <summary>Clé publique présentée par un serveur SSH.</summary>
public sealed record HostKeyInfo(string Host, int Port, string KeyType, byte[] Key)
{
    /// <summary>Empreinte au format OpenSSH : <c>SHA256:…</c> (base64 sans remplissage).</summary>
    public string Fingerprint => ComputeFingerprint(Key);

    /// <summary>« hôte » ou « hôte:port » pour l'affichage.</summary>
    public string Endpoint => Port == 22 ? Host : $"{Host}:{Port}";

    public static string ComputeFingerprint(byte[] key) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=');
}

/// <summary>Résultat de la comparaison d'une clé d'hôte avec le fichier des hôtes connus.</summary>
public enum HostKeyStatus
{
    /// <summary>La clé est celle enregistrée pour cet hôte.</summary>
    Trusted,

    /// <summary>Premier contact : aucune clé enregistrée pour cet hôte.</summary>
    Unknown,

    /// <summary>Une autre clé est enregistrée pour cet hôte : possible attaque de l'homme du milieu.</summary>
    Changed,
}
