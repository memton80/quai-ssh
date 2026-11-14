namespace Quai.Core.Serial;

public enum SerialPortProblem
{
    /// <summary>Aucun port de ce nom sur cet ordinateur (adaptateur débranché, mauvais numéro).</summary>
    NotFound,

    /// <summary>Port déjà ouvert par une autre application (Windows ne le partage pas).</summary>
    InUse,

    /// <summary>Le pilote refuse les paramètres (vitesse, bits de données, parité, bits d'arrêt).</summary>
    SettingsRejected,
}

/// <summary>Le port série n'a pas pu être ouvert.</summary>
public sealed class SerialPortUnavailableException : Exception
{
    public SerialPortUnavailableException(string portName, SerialPortProblem problem, Exception? inner = null)
        : base($"Port série {portName} inutilisable ({problem}).", inner)
    {
        PortName = portName;
        Problem = problem;
    }

    public string PortName { get; }

    public SerialPortProblem Problem { get; }
}
