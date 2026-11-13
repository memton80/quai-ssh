using Quai.Core.Models;

namespace Quai.Core.Services;

/// <summary>Ouvre les sessions d'une connexion du carnet.</summary>
public interface ISessionLauncher
{
    void OpenTerminal(ConnectionProfile profile);
}
