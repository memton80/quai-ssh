using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Quai.Core.Models;

namespace Quai.App;

/// <summary>Fonctions utilisées depuis le XAML (<c>x:Bind</c>).</summary>
public static class UiHelpers
{
    /// <summary>Retrait à gauche proportionnel à la profondeur (arborescence de dossiers).</summary>
    public static Thickness IndentMargin(int depth) => new(Math.Max(0, depth) * 16, 0, 0, 0);

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Raccourci d'un snippet tel qu'affiché (« Ctrl+Maj+1 ») ; vide s'il n'en a pas ou s'il est invalide.</summary>
    public static string SnippetShortcut(string? shortcut) =>
        KeyGesture.TryParse(shortcut, out var gesture) && gesture.IsValid
            ? gesture.Format(Localization.Loc.Get("Key_Ctrl"), Localization.Loc.Get("Key_Shift"), Localization.Loc.Get("Key_Alt"))
            : string.Empty;

    public static InfoBarSeverity WarningIf(bool value) => value ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;

    /// <summary>Texte d'accessibilité et info-bulle du bouton replier / déplier d'un dossier.</summary>
    public static string FolderToggleName(string label, bool expanded) =>
        Localization.Loc.Format(expanded ? "Connections_CollapseFolder" : "Connections_ExpandFolder", label);

    /// <summary>Police du terminal pour l'aperçu des réglages, avec repli sur une police à chasse fixe.</summary>
    public static FontFamily TerminalFont(string? family)
    {
        var name = string.IsNullOrWhiteSpace(family) ? AppSettings.DefaultTerminalFontFamily : family.Trim();
        return new FontFamily($"{name}, Cascadia Mono, Consolas");
    }

    /// <summary>
    /// Taille de l'aperçu, en pixels indépendants : le réglage est en points (comme le terminal),
    /// toujours valide (un NumberBox vidé vaut NaN).
    /// </summary>
    public static double TerminalPreviewFontSize(double points) =>
        (double.IsNaN(points) ? AppSettings.DefaultTerminalFontSize : Math.Clamp(points, AppSettings.MinTerminalFontSize, AppSettings.MaxTerminalFontSize)) * 96 / 72;
}
