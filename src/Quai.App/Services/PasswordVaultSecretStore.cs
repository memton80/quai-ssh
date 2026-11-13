using Quai.Core.Security;
using Windows.Security.Credentials;

namespace Quai.App.Services;

/// <summary>
/// Secrets stockés dans le coffre Windows (<see cref="PasswordVault"/>, visible dans le
/// Gestionnaire d'identification). Rien n'est écrit dans les fichiers de l'application.
/// </summary>
internal sealed class PasswordVaultSecretStore : ISecretStore
{
    private const string Resource = "Quai";
    private const int ElementNotFound = unchecked((int)0x80070490);

    private readonly Lazy<PasswordVault> _vault = new(() => new PasswordVault());

    public string? Get(string key)
    {
        try
        {
            var credential = _vault.Value.Retrieve(Resource, key);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception ex) when (ex.HResult == ElementNotFound)
        {
            return null;
        }
    }

    public void Set(string key, string secret) =>
        _vault.Value.Add(new PasswordCredential(Resource, key, secret));

    /// <summary>Supprime tous les secrets de Quai (désinstallation avec effacement des données).</summary>
    public static void RemoveAll(PasswordVault vault)
    {
        IReadOnlyList<PasswordCredential> credentials;
        try
        {
            credentials = vault.FindAllByResource(Resource);
        }
        catch (Exception ex) when (ex.HResult == ElementNotFound)
        {
            return;
        }

        foreach (var credential in credentials)
        {
            vault.Remove(credential);
        }
    }

    public void Remove(string key)
    {
        try
        {
            _vault.Value.Remove(_vault.Value.Retrieve(Resource, key));
        }
        catch (Exception ex) when (ex.HResult == ElementNotFound)
        {
            // Déjà absent.
        }
    }
}
