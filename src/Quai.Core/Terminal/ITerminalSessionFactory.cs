using Quai.Core.Models;
using Quai.Core.Serial;
using Quai.Core.Ssh;

namespace Quai.Core.Terminal;

/// <summary>Ouvre la session d'un onglet de terminal.</summary>
public interface ITerminalSessionFactory
{
    Task<ITerminalSession> OpenTerminalAsync(
        ConnectionProfile profile,
        int columns,
        int rows,
        CancellationToken cancellationToken = default);
}

/// <summary>Aiguille vers le shell SSH ou le port série selon le type de la connexion.</summary>
public sealed class TerminalSessionFactory : ITerminalSessionFactory
{
    private readonly ISshSessionFactory _ssh;
    private readonly ISerialSessionFactory _serial;

    public TerminalSessionFactory(ISshSessionFactory ssh, ISerialSessionFactory serial)
    {
        _ssh = ssh;
        _serial = serial;
    }

    public Task<ITerminalSession> OpenTerminalAsync(
        ConnectionProfile profile,
        int columns,
        int rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.IsSerial
            ? _serial.OpenAsync(profile.Serial ?? new SerialSettings(), cancellationToken)
            : _ssh.OpenTerminalAsync(profile, columns, rows, cancellationToken);
    }
}
