namespace Quai.Core.Models;

public enum AppTheme
{
    /// <summary>Suit le thème de Windows.</summary>
    System,
    Light,
    Dark,
}

/// <summary>Réglages de l'application (fichier <c>settings.json</c>).</summary>
public sealed class AppSettings
{
    public const string DefaultTerminalFontFamily = "Cascadia Mono";

    /// <summary>Taille en points, comme dans Windows Terminal (même valeur par défaut).</summary>
    public const int DefaultTerminalFontSize = 12;
    public const int MinTerminalFontSize = 8;
    public const int MaxTerminalFontSize = 32;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public string TerminalFontFamily { get; set; } = DefaultTerminalFontFamily;

    public int TerminalFontSize { get; set; } = DefaultTerminalFontSize;

    /// <summary>
    /// Fond du terminal à la couleur de surface de Windows 11 (celle de l'onglet actif) plutôt que le fond
    /// classique de Windows Terminal. Opaque : le moteur ne sait pas dessiner un fond transparent.
    /// </summary>
    public bool TerminalFluentBackground { get; set; } = true;

    /// <summary>Au démarrage, demander à GitHub si une version plus récente est publiée.</summary>
    public bool CheckForUpdates { get; set; } = true;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Ramène les valeurs dans leurs bornes (fichier modifié à la main, ancienne version…).</summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(Theme))
        {
            Theme = AppTheme.System;
        }

        TerminalFontFamily = string.IsNullOrWhiteSpace(TerminalFontFamily)
            ? DefaultTerminalFontFamily
            : TerminalFontFamily.Trim();
        TerminalFontSize = Math.Clamp(TerminalFontSize, MinTerminalFontSize, MaxTerminalFontSize);
    }
}
