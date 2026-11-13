using Microsoft.UI.Xaml;

namespace Quai.App;

/// <summary>Fonctions utilisées depuis le XAML (<c>x:Bind</c>).</summary>
public static class UiHelpers
{
    /// <summary>Retrait à gauche proportionnel à la profondeur (arborescence de dossiers).</summary>
    public static Thickness IndentMargin(int depth) => new(Math.Max(0, depth) * 16, 0, 0, 0);

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}
