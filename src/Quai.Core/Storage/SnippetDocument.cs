using Quai.Core.Models;

namespace Quai.Core.Storage;

/// <summary>Contenu du fichier <c>snippets.json</c>.</summary>
internal sealed class SnippetDocument
{
    public int Version { get; set; } = 1;

    public List<Snippet> Snippets { get; set; } = [];
}
