namespace Quai.Core.Terminal;

/// <summary>Une session interactive affichée dans un terminal : shell SSH distant ou liaison série.</summary>
public interface ITerminalSession : IAsyncDisposable
{
    /// <summary>Octets émis par le shell ou l'appareil (déclenché hors du fil d'interface).</summary>
    event Action<byte[]>? DataReceived;

    /// <summary>Fin de session ; l'exception est nulle pour une fermeture normale.</summary>
    event Action<Exception?>? Closed;

    bool IsOpen { get; }

    /// <summary>La session sait envoyer un signal « break » (liaison série).</summary>
    bool CanSendBreak => false;

    /// <summary>Démarre la réception ; à appeler une fois abonné aux événements.</summary>
    void Start();

    void Send(byte[] data);

    void Resize(int columns, int rows);

    /// <summary>Envoie un signal « break » (ligne maintenue à zéro un court instant) si <see cref="CanSendBreak"/>.</summary>
    void SendBreak()
    {
    }

    /// <summary>
    /// Dossier courant du shell, lu sans rien taper dans le terminal (SSH : canal séparé sur la même connexion).
    /// <c>null</c> si la session ne sait pas le dire.
    /// </summary>
    Task<string?> QueryWorkingDirectoryAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}
