using System.Runtime.InteropServices;
using System.Text;
using Quai.Core.Updates;

namespace Quai.Core.Feedback;

/// <summary>Type de retour : une idée de fonction ou un bug.</summary>
public enum FeedbackKind
{
    Idea,
    Bug,
}

/// <summary>
/// Champ d'un formulaire d'issue GitHub (<c>.github/ISSUE_TEMPLATE/*.yml</c>).
/// </summary>
/// <param name="Id">Identifiant du champ dans le formulaire (paramètre de l'adresse).</param>
/// <param name="Heading">Intitulé, repris dans le texte complet copié en cas de raccourcissement.</param>
/// <param name="Value">Texte saisi ; un champ vide n'est pas transmis.</param>
/// <param name="CanShorten">Texte libre qui peut être coupé si l'adresse est trop longue.</param>
public sealed record FeedbackField(string Id, string Heading, string Value, bool CanShorten = true);

/// <summary>Adresse de la nouvelle issue, pré-remplie.</summary>
/// <param name="Address">Page « nouvelle issue » de GitHub, avec le formulaire rempli.</param>
/// <param name="Shortened">Un texte a été coupé pour que l'adresse reste ouvrable par Windows.</param>
/// <param name="FullText">Texte complet (titre et champs), à coller sur GitHub si un texte a été coupé.</param>
public sealed record FeedbackLink(Uri Address, bool Shortened, string FullText);

/// <summary>
/// Construit l'adresse d'une nouvelle issue GitHub pré-remplie (formulaires d'issue : chaque champ se remplit par son
/// identifiant dans l'adresse). Rien n'est envoyé par Quai : le navigateur s'ouvre et l'utilisateur publie lui-même.
/// </summary>
public static class FeedbackIssue
{
    public const string IdeaTemplate = "idee.yml";
    public const string BugTemplate = "bug.yml";

    /// <summary>
    /// Longueur maximale de l'adresse : Windows (ShellExecute) coupe sans prévenir au-delà de 2 083 caractères
    /// (INTERNET_MAX_URL_LENGTH) ; une marge est gardée.
    /// </summary>
    public const int MaxAddressLength = 2000;

    public const int MaxTitleLength = 120;

    /// <summary>Ajouté à la fin d'un texte coupé.</summary>
    public const string ShortenedMarker = "\n\n[…]";

    /// <summary>Page « nouvelle issue » vierge (repli si le navigateur ne s'ouvre pas).</summary>
    public static Uri NewIssuePage { get; } = new($"https://github.com/{GitHubReleaseSource.Repository}/issues/new/choose");

    public static string TemplateFor(FeedbackKind kind) => kind == FeedbackKind.Bug ? BugTemplate : IdeaTemplate;

    /// <summary>Version de Quai, de Windows et architecture (« Quai 1.1.0 · Microsoft Windows 10.0.26100 · x64 »).</summary>
    public static string DescribeSystem() =>
        $"{AppInfo.Name} {AppInfo.Version} · {RuntimeInformation.OSDescription.Trim()} · {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

    public static FeedbackLink Build(FeedbackKind kind, string title, IReadOnlyList<FeedbackField> fields, int maxLength = MaxAddressLength)
    {
        var cleanTitle = CleanTitle(title);
        var originals = fields.Select(f => NormalizeLines(f.Value).Trim()).ToArray();
        var values = (string[])originals.Clone();
        var prefix = $"https://github.com/{GitHubReleaseSource.Repository}/issues/new?template={Uri.EscapeDataString(TemplateFor(kind))}";
        if (cleanTitle.Length > 0)
        {
            prefix += "&title=" + Uri.EscapeDataString(cleanTitle);
        }

        var address = Compose(prefix, fields, values);
        var shortened = false;
        while (address.Length > maxLength)
        {
            // Le texte libre le plus long (une fois encodé) est raccourci en premier.
            var index = -1;
            var longest = 0;
            for (var i = 0; i < fields.Count; i++)
            {
                var length = Uri.EscapeDataString(values[i]).Length;
                if (fields[i].CanShorten && length > longest)
                {
                    index = i;
                    longest = length;
                }
            }

            if (index < 0)
            {
                break;
            }

            var budget = longest - (address.Length - maxLength);
            values[index] = Cut(originals[index], budget);
            shortened = true;
            address = Compose(prefix, fields, values);
        }

        return new FeedbackLink(new Uri(address), shortened, FullText(cleanTitle, fields, originals));
    }

    /// <summary>Titre sur une ligne, sans espaces superflus, limité à <see cref="MaxTitleLength"/> caractères.</summary>
    internal static string CleanTitle(string? title)
    {
        var line = string.Join(' ', (title ?? string.Empty).Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return Truncate(line, MaxTitleLength);
    }

    /// <summary>Les zones de texte WinUI séparent les lignes par <c>\r</c> : tout est ramené à <c>\n</c>.</summary>
    internal static string NormalizeLines(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string Compose(string prefix, IReadOnlyList<FeedbackField> fields, string[] values)
    {
        var builder = new StringBuilder(prefix);
        for (var i = 0; i < fields.Count; i++)
        {
            if (values[i].Length > 0)
            {
                builder.Append('&').Append(Uri.EscapeDataString(fields[i].Id)).Append('=').Append(Uri.EscapeDataString(values[i]));
            }
        }

        return builder.ToString();
    }

    /// <summary>Le plus long début du texte qui, suivi de <see cref="ShortenedMarker"/>, tient dans le budget une fois encodé.</summary>
    private static string Cut(string text, int budget)
    {
        if (Uri.EscapeDataString(ShortenedMarker).Length > budget)
        {
            return string.Empty;
        }

        int low = 0, high = text.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Uri.EscapeDataString(Shorten(text, middle)).Length <= budget)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Shorten(text, low);
    }

    private static string Shorten(string text, int length) => Truncate(text, length).TrimEnd() + ShortenedMarker;

    /// <summary>Coupe sans séparer une paire de substitution (emoji, caractères hors plan de base).</summary>
    private static string Truncate(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return text[..length];
    }

    private static string FullText(string title, IReadOnlyList<FeedbackField> fields, string[] values)
    {
        var builder = new StringBuilder(title);
        for (var i = 0; i < fields.Count; i++)
        {
            if (values[i].Length > 0)
            {
                if (builder.Length > 0)
                {
                    builder.Append("\n\n");
                }

                builder.Append("### ").Append(fields[i].Heading).Append('\n').Append(values[i]);
            }
        }

        return builder.ToString();
    }
}
