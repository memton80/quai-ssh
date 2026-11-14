namespace Quai.Core.Terminal;

/// <summary>
/// Suit, dans le flux envoyé par le serveur, les modes privés DEC dont l'application a besoin
/// (collage « bracketed », suivi de la souris) : le moteur natif de Windows Terminal ne les expose pas.
/// Automate minimal : seules les séquences <c>ESC [ ? n ; … h|l</c> et <c>ESC c</c> sont reconnues,
/// y compris lorsqu'elles arrivent coupées entre deux paquets.
/// </summary>
public sealed class TerminalModeTracker
{
    private const int BracketedPasteMode = 2004;
    private const int MaxParameter = 100_000;

    // 9 = X10, 1000 = normal, 1001 = surlignage, 1002 = mouvements bouton enfoncé, 1003 = tous les mouvements.
    private static readonly int[] MouseModes = [9, 1000, 1001, 1002, 1003];

    private readonly HashSet<int> _mouseModes = [];
    private readonly List<int> _parameters = [];
    private State _state;
    private int _current = -1;

    private enum State
    {
        Ground,
        Escape,
        CsiEntry,
        PrivateParameters,
    }

    /// <summary>Le serveur attend le texte collé entre <c>ESC[200~</c> et <c>ESC[201~</c>.</summary>
    public bool BracketedPaste { get; private set; }

    /// <summary>Une application distante (vim, htop…) reçoit les clics de souris.</summary>
    public bool MouseTracking => _mouseModes.Count > 0;

    public void Feed(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            Step(c);
        }
    }

    /// <summary>Nouvelle session : tous les modes reviennent à leur état initial.</summary>
    public void Reset()
    {
        BracketedPaste = false;
        _mouseModes.Clear();
        _parameters.Clear();
        _current = -1;
        _state = State.Ground;
    }

    private void Step(char c)
    {
        switch (_state)
        {
            case State.Ground:
                if (c == '\u001b')
                {
                    _state = State.Escape;
                }

                break;

            case State.Escape:
                _state = c switch
                {
                    '[' => State.CsiEntry,
                    '\u001b' => State.Escape,
                    _ => State.Ground,
                };
                if (c == 'c')
                {
                    // RIS : réinitialisation complète du terminal.
                    Reset();
                }

                break;

            case State.CsiEntry:
                if (c == '?')
                {
                    _parameters.Clear();
                    _current = -1;
                    _state = State.PrivateParameters;
                }
                else
                {
                    _state = c == '\u001b' ? State.Escape : State.Ground;
                }

                break;

            case State.PrivateParameters:
                if (c is >= '0' and <= '9')
                {
                    _current = Math.Min(MaxParameter, Math.Max(_current, 0) * 10 + (c - '0'));
                }
                else if (c == ';')
                {
                    _parameters.Add(_current);
                    _current = -1;
                }
                else if (c is 'h' or 'l')
                {
                    _parameters.Add(_current);
                    Apply(c == 'h');
                    _state = State.Ground;
                }
                else
                {
                    _state = c == '\u001b' ? State.Escape : State.Ground;
                }

                break;
        }
    }

    private void Apply(bool enabled)
    {
        foreach (var mode in _parameters)
        {
            if (mode == BracketedPasteMode)
            {
                BracketedPaste = enabled;
            }
            else if (Array.IndexOf(MouseModes, mode) >= 0)
            {
                if (enabled)
                {
                    _mouseModes.Add(mode);
                }
                else
                {
                    _mouseModes.Remove(mode);
                }
            }
        }

        _parameters.Clear();
        _current = -1;
    }
}
