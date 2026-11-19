using Quai.Core.Local;
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

/// <summary>Aiguille vers le shell SSH, le port série ou le shell local selon le type de la connexion.</summary>
public sealed class TerminalSessionFactory : ITerminalSessionFactory
{
    private readonly ISshSessionFactory _ssh;
    private readonly ISerialSessionFactory _serial;
    private readonly ILocalTerminalFactory? _local;

    public TerminalSessionFactory(ISshSessionFactory ssh, ISerialSessionFactory serial, ILocalTerminalFactory? local = null)
    {
        _ssh = ssh;
        _serial = serial;
        _local = local;
    }

    public Task<ITerminalSession> OpenTerminalAsync(
        ConnectionProfile profile,
        int columns,
        int rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Protocol switch
        {
            ConnectionProtocol.Serial => _serial.OpenAsync(profile.Serial ?? new SerialSettings(), cancellationToken),
            ConnectionProtocol.Local when _local is not null && profile.LocalShell is { } shell =>
                _local.OpenAsync(shell, columns, rows, cancellationToken),
            ConnectionProtocol.Local => throw new InvalidOperationException("Aucun shell local à lancer."),
            _ => _ssh.OpenTerminalAsync(profile, columns, rows, cancellationToken),
        };
    }
}
