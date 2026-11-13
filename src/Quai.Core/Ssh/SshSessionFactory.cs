using Quai.Core.Models;
using Renci.SshNet;

namespace Quai.Core.Ssh;

/// <summary>Ouvre les sessions (terminal, SFTP) d'une connexion du carnet.</summary>
public interface ISshSessionFactory
{
    Task<ITerminalSession> OpenTerminalAsync(
        ConnectionProfile profile,
        int columns,
        int rows,
        CancellationToken cancellationToken = default);
}

public sealed class SshSessionFactory : ISshSessionFactory
{
    private readonly SshConnector _connector;

    public SshSessionFactory(SshConnector connector)
    {
        _connector = connector;
    }

    public async Task<ITerminalSession> OpenTerminalAsync(
        ConnectionProfile profile,
        int columns,
        int rows,
        CancellationToken cancellationToken = default)
    {
        var client = await _connector.ConnectAsync(profile, info => new SshClient(info), cancellationToken);
        try
        {
            var stream = await Task.Run(
                () => client.CreateShellStream(
                    SshTerminalSession.TerminalName,
                    (uint)Math.Max(columns, 1),
                    (uint)Math.Max(rows, 1),
                    0,
                    0,
                    64 * 1024),
                cancellationToken);
            return new SshTerminalSession(client, stream);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
