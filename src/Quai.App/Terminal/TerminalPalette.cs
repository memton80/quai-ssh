using System.Globalization;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Quai.App.Terminal;

/// <summary>Couleurs du terminal, une palette par thème de l'application.</summary>
internal static class TerminalPalette
{
    // Fond « Fluent » : SolidBackgroundFillColorTertiary de WinUI, la couleur de l'onglet actif et de la zone de
    // contenu du TabView (TabViewItemHeaderBackgroundSelected) ; le terminal se fond ainsi dans son onglet.
    private const string FluentDarkBackground = "#282828";
    private const string FluentLightBackground = "#f9f9f9";

    // Thème sombre : palette « Campbell » de Windows Terminal.
    private static readonly Palette Dark = new(
        Background: "#0c0c0c",
        Foreground: "#cccccc",
        Selection: "#ffffff",
        Table:
        [
            "#0c0c0c", "#c50f1f", "#13a10e", "#c19c00", "#0037da", "#881798", "#3a96dd", "#cccccc",
            "#767676", "#e74856", "#16c60c", "#f9f1a5", "#3b78ff", "#b4009e", "#61d6d6", "#f2f2f2",
        ]);

    // Thème clair : couleurs lisibles sur fond blanc cassé.
    private static readonly Palette Light = new(
        Background: "#fafafa",
        Foreground: "#383a42",
        Selection: "#0078d4",
        Table:
        [
            "#383a42", "#e45649", "#3d8b3c", "#a8730a", "#0184bc", "#a626a4", "#0997b3", "#a0a1a7",
            "#4f525d", "#df6c75", "#50a14f", "#c18301", "#4078f2", "#c577dd", "#56b5c1", "#090a0b",
        ]);

    /// <summary>Thème transmis au moteur de Windows Terminal.</summary>
    /// <param name="fluent">Fond à la couleur de surface de Windows 11 plutôt que celui de la palette.</param>
    public static TerminalNative.TerminalTheme For(ElementTheme theme, bool fluent)
    {
        var palette = Pick(theme);
        return new TerminalNative.TerminalTheme
        {
            DefaultBackground = ToColorRef(BackgroundOf(theme, fluent)),
            DefaultForeground = ToColorRef(palette.Foreground),
            DefaultSelectionBackground = ToColorRef(palette.Selection),
            CursorStyle = TerminalNative.CursorStyle.BlinkingBar,
            ColorTable = [.. palette.Table.Select(ToColorRef)],
        };
    }

    /// <summary>Fond du terminal, pour la marge XAML qui l'entoure.</summary>
    public static Color Background(ElementTheme theme, bool fluent)
    {
        var (r, g, b) = Parse(BackgroundOf(theme, fluent));
        return Color.FromArgb(255, r, g, b);
    }

    private static Palette Pick(ElementTheme theme) => theme == ElementTheme.Light ? Light : Dark;

    private static string BackgroundOf(ElementTheme theme, bool fluent) => fluent
        ? theme == ElementTheme.Light ? FluentLightBackground : FluentDarkBackground
        : Pick(theme).Background;

    private static uint ToColorRef(string hex)
    {
        var (r, g, b) = Parse(hex);
        return (uint)(r | (g << 8) | (b << 16));
    }

    private static (byte R, byte G, byte B) Parse(string hex)
    {
        var value = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    private sealed record Palette(string Background, string Foreground, string Selection, string[] Table);
}
