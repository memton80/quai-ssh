using System.Text;

namespace Quai.Core.Terminal;

/// <summary>
/// Dossier courant annoncé par le shell distant dans le flux du terminal : séquence OSC 7
/// (<c>ESC ] 7 ; file://hôte/chemin BEL</c>, émise par fish, par zsh / bash configurés pour, par le
/// <c>vte.sh</c> de nombreuses distributions) ou OSC 1337 <c>CurrentDir=</c> (iTerm2). Automate sur les octets,
/// tolérant aux séquences coupées entre deux paquets ; le reste du flux est ignoré.
/// </summary>
public sealed class WorkingDirectoryTracker
{
    private const byte Escape = 0x1B;
    private const byte Bell = 0x07;
    private const int MaxSequence = 4096;

    private readonly List<byte> _payload = [];
    private State _state;
    private volatile string? _current;

    private enum State
    {
        Ground,
        Escape,
        Osc,
        OscEscape,
    }

    /// <summary>Dernier dossier annoncé, ou <c>null</c> si le shell n'en a annoncé aucun.</summary>
    public string? Current => _current;

    /// <summary>Nouvelle session : on oublie le dossier de la précédente.</summary>
    public void Reset()
    {
        _current = null;
        _payload.Clear();
        _state = State.Ground;
    }

    public void Feed(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            Step(b);
        }
    }

    private void Step(byte b)
    {
        switch (_state)
        {
            case State.Ground:
                if (b == Escape)
                {
                    _state = State.Escape;
                }

                break;

            case State.Escape:
                if (b == (byte)']')
                {
                    _payload.Clear();
                    _state = State.Osc;
                }
                else
                {
                    _state = b == Escape ? State.Escape : State.Ground;
                }

                break;

            case State.Osc:
                if (b == Bell)
                {
                    Complete();
                }
                else if (b == Escape)
                {
                    _state = State.OscEscape;
                }
                else if (_payload.Count >= MaxSequence)
                {
                    // Séquence anormalement longue : abandonnée.
                    _payload.Clear();
                    _state = State.Ground;
                }
                else
                {
                    _payload.Add(b);
                }

                break;

            case State.OscEscape:
                if (b == (byte)'\\')
                {
                    // ESC \ (ST) termine la séquence.
                    Complete();
                }
                else
                {
                    // ESC suivi d'autre chose : séquence interrompue, ce ESC en commence peut-être une autre.
                    _payload.Clear();
                    _state = State.Escape;
                    Step(b);
                }

                break;
        }
    }

    private void Complete()
    {
        _state = State.Ground;
        var text = Encoding.UTF8.GetString([.. _payload]);
        _payload.Clear();
        if (Parse(text) is { } directory)
        {
            _current = directory;
        }
    }

    /// <summary>Contenu d'une séquence OSC (sans <c>ESC ]</c> ni terminateur) vers un chemin absolu, ou <c>null</c>.</summary>
    internal static string? Parse(string osc)
    {
        if (osc.StartsWith("7;", StringComparison.Ordinal))
        {
            return FromFileUri(osc[2..]);
        }

        if (osc.StartsWith("1337;CurrentDir=", StringComparison.Ordinal))
        {
            return Validate(osc["1337;CurrentDir=".Length..]);
        }

        return null;
    }

    private static string? FromFileUri(string uri)
    {
        const string Scheme = "file://";
        if (!uri.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // file://hôte/chemin : l'hôte (nom du serveur, parfois vide) est ignoré, le chemin est encodé en URL.
        var slash = uri.IndexOf('/', Scheme.Length);
        if (slash < 0)
        {
            return null;
        }

        try
        {
            return Validate(Uri.UnescapeDataString(uri[slash..]));
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static string? Validate(string path) =>
        path.StartsWith('/') && !path.Any(char.IsControl) ? path : null;
}
