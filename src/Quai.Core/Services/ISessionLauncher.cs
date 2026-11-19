using Quai.Core.Models;
using Quai.Core.ViewModels;

namespace Quai.Core.Services;

/// <summary>Ouvre les sessions d'une connexion du carnet.</summary>
public interface ISessionLauncher
{
    void OpenTerminal(ConnectionProfile profile);

    /// <summary>Terminal local (shell de cet ordinateur) ; <paramref name="shellId"/> nul = shell par défaut des réglages.</summary>
    void OpenLocalTerminal(string? shellId = null);

    void OpenSftp(ConnectionProfile profile);

    /// <summary>Explorateur SFTP de la connexion d'un terminal, ouvert dans le dossier courant de son shell.</summary>
    Task OpenSftpHereAsync(TerminalTabViewModel tab);
}
