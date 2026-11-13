using Windows.Security.Credentials;

namespace Quai.App.Services;

/// <summary>
/// <c>Quai.exe --purge-secrets</c> : efface du coffre Windows tous les secrets de Quai, sans ouvrir de fenêtre.
/// Appelé par le désinstallateur quand l'utilisateur choisit de supprimer aussi ses données.
/// </summary>
internal static class SecretPurge
{
    private const string Switch = "--purge-secrets";

    public static bool IsRequested() =>
        Environment.GetCommandLineArgs().Any(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));

    /// <summary>Retourne 0 si le coffre ne contient plus aucun secret de Quai.</summary>
    public static int Run()
    {
        try
        {
            PasswordVaultSecretStore.RemoveAll(new PasswordVault());
            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
