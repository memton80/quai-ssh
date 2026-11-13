namespace Quai.Core.Security;

/// <summary>
/// Coffre à secrets (mots de passe, passphrases). L'implémentation de l'application s'appuie sur le
/// coffre Windows ; rien n'est jamais écrit en clair sur disque ni dans les journaux.
/// </summary>
public interface ISecretStore
{
    /// <summary>Retourne le secret, ou <c>null</c> s'il n'existe pas.</summary>
    string? Get(string key);

    void Set(string key, string secret);

    /// <summary>Supprime le secret s'il existe (sans erreur sinon).</summary>
    void Remove(string key);
}
