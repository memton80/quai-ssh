using System.Text.Json.Serialization;

namespace Quai.Core.Models;

/// <summary>
/// Commande enregistrée, envoyée dans un terminal en deux touches (<c>Ctrl+Maj+P</c>). Valable pour toutes les
/// connexions, ou pour une seule. Ne doit contenir aucun secret : le fichier <c>snippets.json</c> est en clair.
/// </summary>
public sealed class Snippet
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Texte envoyé au shell ; plusieurs lignes = plusieurs commandes, chacune validée par Entrée.</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Connexion du carnet à laquelle le snippet est réservé ; <c>null</c> = toutes les connexions.</summary>
    public Guid? ConnectionId { get; set; }

    /// <summary>Appuyer sur Entrée après la dernière ligne (sinon, la commande attend d'être complétée ou validée).</summary>
    public bool Run { get; set; } = true;

    /// <summary>
    /// Raccourci clavier qui envoie ce snippet dans le terminal (forme de <see cref="KeyGesture.ToString"/>,
    /// « Ctrl+Shift+1 ») ; <c>null</c> = aucun.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Shortcut { get; set; }

    /// <summary>Raccourci lu et valide ; <c>null</c> si aucun, ou s'il ne respecte pas les règles (fichier modifié à la main).</summary>
    [JsonIgnore]
    public KeyGesture? Gesture => KeyGesture.TryParse(Shortcut, out var gesture) && gesture.IsValid ? gesture : null;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? FirstLine : Name;

    /// <summary>Première ligne de la commande (aperçu d'une ligne dans les listes).</summary>
    [JsonIgnore]
    public string FirstLine
    {
        get
        {
            var text = Command.TrimStart('\r', '\n');
            var end = text.IndexOfAny(['\r', '\n']);
            return end < 0 ? text : text[..end] + " …";
        }
    }

    public bool AppliesTo(Guid connectionId) => ConnectionId is null || ConnectionId == connectionId;

    /// <summary>
    /// Les deux snippets peuvent servir dans un même onglet (l'un des deux vaut pour toutes les connexions, ou ils
    /// visent la même) : ils ne peuvent pas partager un raccourci. Deux connexions différentes, si.
    /// </summary>
    public bool OverlapsWith(Snippet other) =>
        ConnectionId is null || other.ConnectionId is null || ConnectionId == other.ConnectionId;

    public Snippet Clone() => (Snippet)MemberwiseClone();

    public void Normalize()
    {
        Name = Name.Trim();
        Command = Command.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
        if (ConnectionId == Guid.Empty)
        {
            ConnectionId = null;
        }

        // Forme canonique, ou rien : un raccourci illisible ou interdit ne doit jamais voler une touche au shell.
        Shortcut = Gesture?.ToString();
    }

    /// <summary>
    /// Ce qui est tapé dans le terminal : chaque ligne suivie d'Entrée (CR, comme le clavier), la dernière seulement
    /// si <see cref="Run"/>.
    /// </summary>
    public string ToTerminalInput()
    {
        var command = Command.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
        var input = command.Replace('\n', '\r');
        return Run && input.Length > 0 ? input + "\r" : input;
    }
}
