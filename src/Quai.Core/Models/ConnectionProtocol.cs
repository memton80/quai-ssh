namespace Quai.Core.Models;

/// <summary>Type de connexion : serveur SSH distant, port série ou shell de cet ordinateur.</summary>
public enum ConnectionProtocol
{
    Ssh,
    Serial,

    /// <summary>Shell local (PowerShell, invite de commandes, WSL…) dans une pseudo-console Windows (ConPTY).</summary>
    Local,
}
