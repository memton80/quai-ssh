namespace Quai.Core.Models;

/// <summary>Pourquoi une combinaison de touches ne peut pas servir de raccourci de snippet.</summary>
public enum KeyGestureProblem
{
    None,

    /// <summary>Touche non prise en charge (seules les lettres, les chiffres et F1 à F24 le sont).</summary>
    UnsupportedKey,

    /// <summary>Sans Ctrl, Alt ni Maj : la touche serait tapée normalement.</summary>
    NeedsModifier,

    /// <summary>Ctrl+Alt = AltGr sur les claviers européens (@, #, €…) : jamais un raccourci.</summary>
    AltGr,

    /// <summary>Lettre ou chiffre sans Maj + (Ctrl ou Alt) : la combinaison appartient au shell (Ctrl+C, Ctrl+R, Alt+B…).</summary>
    ShellKey,

    /// <summary>Raccourci de Quai (copier, coller, snippets, terminal local, fermeture de la fenêtre).</summary>
    Reserved,
}

/// <summary>
/// Combinaison de touches d'un snippet (<c>Ctrl+Maj+1</c>, <c>Alt+F5</c>…). Touche = code de touche virtuelle Windows,
/// indépendant de la disposition du clavier pour les chiffres et les F1 à F24. Écrite en texte (« Ctrl+Shift+1 ») dans
/// <c>snippets.json</c>.
/// </summary>
public readonly record struct KeyGesture(bool Control, bool Shift, bool Alt, int Key)
{
    private const int KeyA = 0x41;
    private const int KeyZ = 0x5A;
    private const int Key0 = 0x30;
    private const int Key9 = 0x39;
    private const int KeyF1 = 0x70;
    private const int KeyF24 = 0x87;
    private const int KeyF4 = 0x73;

    public bool IsLetter => Key is >= KeyA and <= KeyZ;

    public bool IsDigit => Key is >= Key0 and <= Key9;

    public bool IsFunctionKey => Key is >= KeyF1 and <= KeyF24;

    public static bool IsSupportedKey(int key) => key is >= KeyA and <= KeyZ or >= Key0 and <= Key9 or >= KeyF1 and <= KeyF24;

    /// <summary>Nom de la touche seule : « A », « 7 », « F5 ».</summary>
    public string KeyName => IsFunctionKey ? $"F{Key - KeyF1 + 1}" : IsSupportedKey(Key) ? ((char)Key).ToString() : $"#{Key}";

    /// <summary>
    /// Règles, dans l'ordre : touche prise en charge, au moins un modificateur, jamais Ctrl+Alt (AltGr), ni une
    /// combinaison dont le shell a besoin, ni un raccourci de Quai.
    /// </summary>
    public KeyGestureProblem Problem
    {
        get
        {
            if (!IsSupportedKey(Key))
            {
                return KeyGestureProblem.UnsupportedKey;
            }

            if (!Control && !Alt && !Shift)
            {
                return KeyGestureProblem.NeedsModifier;
            }

            if (Control && Alt)
            {
                return KeyGestureProblem.AltGr;
            }

            // Lettre ou chiffre : toujours Maj + (Ctrl ou Alt). Le reste appartient au shell ou au clavier : Ctrl+lettre
            // (^C, ^R, ^D…), Alt+lettre (readline, emacs), Ctrl+chiffre (Ctrl+3 = Échap, Ctrl+2 = NUL), Alt+chiffre
            // (fenêtres d'irssi, weechat), Maj seul (majuscule, ou symbole : & é " en AZERTY).
            if ((IsLetter || IsDigit) && (!Shift || (!Control && !Alt)))
            {
                return KeyGestureProblem.ShellKey;
            }

            // Ctrl+Maj+C copie, Ctrl+Maj+V colle, Ctrl+Maj+P ouvre les snippets, Ctrl+Maj+T ouvre un terminal local,
            // Alt+F4 ferme la fenêtre.
            if ((Control && Shift && !Alt && Key is 'C' or 'V' or 'P' or 'T') || (Alt && !Control && !Shift && Key == KeyF4))
            {
                return KeyGestureProblem.Reserved;
            }

            return KeyGestureProblem.None;
        }
    }

    public bool IsValid => Problem == KeyGestureProblem.None;

    /// <summary>Forme enregistrée : « Ctrl+Shift+Alt+F5 » (modificateurs dans cet ordre, noms anglais stables).</summary>
    public override string ToString() => Format("Ctrl", "Shift", "Alt");

    /// <summary>Forme affichée, avec les noms des touches de la langue de l'interface (« Ctrl+Maj+1 »).</summary>
    public string Format(string control, string shift, string alt)
    {
        var parts = new List<string>(4);
        if (Control)
        {
            parts.Add(control);
        }

        if (Shift)
        {
            parts.Add(shift);
        }

        if (Alt)
        {
            parts.Add(alt);
        }

        parts.Add(KeyName);
        return string.Join('+', parts);
    }

    /// <summary>Lit la forme enregistrée (« Ctrl+Shift+1 ») ; casse et espaces ignorés, « Maj » accepté.</summary>
    public static bool TryParse(string? text, out KeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        bool control = false, shift = false, alt = false;
        foreach (var modifier in parts[..^1])
        {
            switch (modifier.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    control = true;
                    break;
                case "SHIFT" or "MAJ":
                    shift = true;
                    break;
                case "ALT":
                    alt = true;
                    break;
                default:
                    return false;
            }
        }

        var name = parts[^1].ToUpperInvariant();
        int key;
        if (name.Length == 1 && (char.IsAsciiLetterUpper(name[0]) || char.IsAsciiDigit(name[0])))
        {
            key = name[0];
        }
        else if (name.Length is 2 or 3 && name[0] == 'F' && int.TryParse(name.AsSpan(1), out var number) && number is >= 1 and <= 24
            && !name.AsSpan(1).StartsWith("0"))
        {
            key = KeyF1 + number - 1;
        }
        else
        {
            return false;
        }

        gesture = new KeyGesture(control, shift, alt, key);
        return true;
    }
}
