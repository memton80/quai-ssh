using System.Globalization;

namespace Quai.Core.Models;

/// <summary>
/// Adresse saisie dans le champ « Connexion rapide » du carnet : <c>utilisateur@hôte:port</c>, sous ses formes
/// courantes — <c>hôte</c>, <c>utilisateur@hôte</c>, <c>[::1]:2222</c>, <c>ssh://utilisateur@hôte:port</c>, ou une
/// commande collée telle quelle (<c>ssh -p 2222 utilisateur@hôte</c>, <c>ssh -l utilisateur hôte</c>).
/// </summary>
public sealed record QuickConnectTarget(string? Username, string Host, int Port)
{
    private const string UriScheme = "ssh://";

    /// <summary>Utilisateur absent de la saisie : il faudra le demander.</summary>
    public bool HasUsername => !string.IsNullOrEmpty(Username);

    /// <summary>Profil temporaire (absent du carnet) pour ouvrir un terminal sur cette adresse.</summary>
    public ConnectionProfile ToProfile(string? username = null)
    {
        var profile = new ConnectionProfile
        {
            Host = Host,
            Port = Port,
            Username = username ?? Username ?? string.Empty,
            Authentication = AuthenticationKind.Password,
            IsTransient = true,
        };
        profile.Normalize();
        return profile;
    }

    public static bool TryParse(string? text, out QuickConnectTarget target)
    {
        target = null!;
        var tokens = (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        // Commande collée : « ssh [-p port] [-l utilisateur] destination ».
        var index = string.Equals(tokens[0], "ssh", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        int? optionPort = null;
        string? optionUser = null;
        string? destination = null;
        for (; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token is "-p" or "-l")
            {
                if (index + 1 >= tokens.Length)
                {
                    return false;
                }

                var value = tokens[++index];
                if (token == "-p")
                {
                    if (!TryParsePort(value, out var parsed))
                    {
                        return false;
                    }

                    optionPort = parsed;
                }
                else
                {
                    optionUser = value;
                }
            }
            else if (token.StartsWith('-') || destination is not null)
            {
                // Autre option de ssh, ou commande à exécuter après la destination : non pris en charge.
                return false;
            }
            else
            {
                destination = token;
            }
        }

        if (destination is null || !TryParseDestination(destination, out var user, out var host, out var port))
        {
            return false;
        }

        var username = optionUser ?? user;
        if (username is not null && !IsValidUsername(username))
        {
            return false;
        }

        target = new QuickConnectTarget(username, host, optionPort ?? port ?? ConnectionProfile.DefaultPort);
        return true;
    }

    private static bool TryParseDestination(string text, out string? user, out string host, out int? port)
    {
        user = null;
        host = string.Empty;
        port = null;

        if (text.StartsWith(UriScheme, StringComparison.OrdinalIgnoreCase))
        {
            text = text[UriScheme.Length..].TrimEnd('/');
        }

        // L'utilisateur s'arrête au dernier « @ » (un « @ » peut figurer dans un identifiant d'annuaire).
        var at = text.LastIndexOf('@');
        if (at >= 0)
        {
            user = text[..at];
            text = text[(at + 1)..];
            if (user.Length == 0)
            {
                return false;
            }
        }

        if (text.StartsWith('['))
        {
            // IPv6 entre crochets, suivie éventuellement de « :port ».
            var close = text.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                return false;
            }

            host = text[1..close];
            var rest = text[(close + 1)..];
            if (rest.Length > 0)
            {
                if (!rest.StartsWith(':') || !TryParsePort(rest[1..], out var parsed))
                {
                    return false;
                }

                port = parsed;
            }

            return host.Contains(':', StringComparison.Ordinal) && IsValidHost(host);
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0 && colon == text.LastIndexOf(':'))
        {
            // Un seul « : » : hôte et port.
            if (!TryParsePort(text[(colon + 1)..], out var parsed))
            {
                return false;
            }

            port = parsed;
            text = text[..colon];
        }

        // Plusieurs « : » sans crochets : adresse IPv6 sans port.
        host = text;
        return IsValidHost(host);
    }

    private static bool TryParsePort(string text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;

    private static bool IsValidHost(string host) =>
        host.Length is > 0 and <= 253
        && host.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ':' or '%');

    private static bool IsValidUsername(string user) =>
        user.Length is > 0 and <= 256 && !user.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ':' or '/');
}
