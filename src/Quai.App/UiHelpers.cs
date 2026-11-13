using Microsoft.UI.Xaml;
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

    /// <summary>Police du terminal pour l'aperçu des réglages, avec repli sur une police à chasse fixe.</summary>
    public static FontFamily TerminalFont(string? family)
    {
        var name = string.IsNullOrWhiteSpace(family) ? AppSettings.DefaultTerminalFontFamily : family.Trim();
        return new FontFamily($"{name}, Cascadia Mono, Consolas");
    }

    /// <summary>Taille de police toujours valide (un NumberBox vidé vaut NaN).</summary>
    public static double SafeFontSize(double size) =>
        double.IsNaN(size) ? AppSettings.DefaultTerminalFontSize : Math.Clamp(size, AppSettings.MinTerminalFontSize, AppSettings.MaxTerminalFontSize);
}
