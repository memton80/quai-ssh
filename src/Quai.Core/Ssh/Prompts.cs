using Quai.Core.Models;

namespace Quai.Core.Ssh;

/// <summary>Décision de l'utilisateur sur une clé d'hôte (implémentée par l'interface).</summary>
public interface IHostKeyPrompt
{
    /// <summary>Premier contact : faire confiance à cette clé ?</summary>
    Task<bool> ConfirmUnknownHostAsync(HostKeyInfo presented);

    /// <summary>
    /// La clé ne correspond plus à celle enregistrée. Doit afficher une alerte claire ;
    /// ne retourne vrai que si l'utilisateur choisit explicitement de remplacer la clé enregistrée.
    /// </summary>
    Task<bool> ConfirmChangedHostAsync(HostKeyInfo presented, IReadOnlyList<HostKeyInfo> known);
}

/// <summary>Secret saisi par l'utilisateur ; <see cref="Remember"/> = le mémoriser dans le coffre.</summary>
public sealed record SecretInput(string Secret, bool Remember);

public enum SecretKind
{
    Password,
    Passphrase,
}

/// <summary>Demande d'un mot de passe ou d'une passphrase (implémentée par l'interface).</summary>
public interface ICredentialPrompt
{
    /// <summary>Retourne <c>null</c> si l'utilisateur annule. <paramref name="isRetry"/> : la saisie précédente a été refusée.</summary>
    Task<SecretInput?> PromptSecretAsync(ConnectionProfile profile, SecretKind kind, bool isRetry);
}
