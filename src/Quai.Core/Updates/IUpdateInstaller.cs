namespace Quai.Core.Updates;

/// <summary>Côté application : ce que Quai.Core ne fait pas lui-même (registre, processus, fenêtre, navigateur).</summary>
public interface IUpdateInstaller
{
    /// <summary>Quai tourne depuis son installation : l'installateur peut le mettre à jour (pas une copie portable).</summary>
    bool CanInstall { get; }

    /// <summary>
    /// Ferme les sessions ouvertes (après confirmation), lance l'installateur vérifié en mode silencieux puis quitte
    /// Quai ; l'installateur relance la nouvelle version une fois installée.
    /// </summary>
    /// <returns><c>false</c> si l'utilisateur préfère garder ses sessions ouvertes : rien n'est lancé.</returns>
    Task<bool> InstallAndRestartAsync(string installerPath);

    /// <summary>Ouvre la page de la release dans le navigateur (copie portable, installateur absent de la release).</summary>
    void OpenReleasePage(Uri page);
}
