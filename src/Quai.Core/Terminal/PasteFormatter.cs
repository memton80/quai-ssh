namespace Quai.Core.Terminal;

/// <summary>Prépare un texte du presse-papiers avant de l'envoyer au shell distant.</summary>
public static class PasteFormatter
{
    private const string BracketStart = "\u001b[200~";
    private const string BracketEnd = "\u001b[201~";

    /// <summary>
    /// Fins de ligne Windows ramenées à un simple retour chariot (une ligne collée = une touche Entrée),
    /// et, si le serveur l'a demandé, texte encadré par les marqueurs « bracketed paste » : le shell
    /// n'exécute alors rien avant que l'utilisateur ait validé.
    /// </summary>
    public static string Format(string text, bool bracketed)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r');
        if (!bracketed)
        {
            return normalized;
        }

        // Le texte collé ne doit pas pouvoir refermer lui-même l'encadrement.
        normalized = normalized.Replace(BracketEnd, string.Empty, StringComparison.Ordinal);
        return BracketStart + normalized + BracketEnd;
    }
}
