using Quai.Core.Models;

namespace Quai.Core.Storage;

/// <summary>Contenu du fichier <c>connections.json</c>.</summary>
internal sealed class ConnectionDocument
{
    public int Version { get; set; } = 1;

    public List<ConnectionProfile> Connections { get; set; } = [];
}
