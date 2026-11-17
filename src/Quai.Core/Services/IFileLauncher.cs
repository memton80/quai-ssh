namespace Quai.Core.Services;

/// <summary>Ouvre un fichier local dans une application de Windows (implémenté par l'interface).</summary>
public interface IFileLauncher
{
    /// <summary>
    /// Ouvre le fichier avec l'application associée à son type, ou laisse choisir l'application
    /// (<paramref name="chooseApplication"/>, ou type sans application associée). Retourne faux si rien n'a pu être lancé.
    /// </summary>
    Task<bool> OpenAsync(string path, bool chooseApplication);
}
