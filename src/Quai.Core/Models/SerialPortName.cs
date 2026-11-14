using System.Globalization;

namespace Quai.Core.Models;

/// <summary>
/// Noms de ports série Windows. <c>System.IO.Ports</c> n'ouvre que les noms de la forme <c>COM</c> + numéro
/// (<c>COM3</c>, <c>COM12</c>) : c'est donc la seule forme acceptée.
/// </summary>
public static class SerialPortName
{
    private const string Prefix = "COM";

    /// <summary>Supprime les espaces et met en majuscules : « com3 » devient « COM3 ».</summary>
    public static string Normalize(string? name) =>
        string.Concat((name ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '\0')).ToUpperInvariant();

    /// <summary>Vrai pour <c>COM1</c>, <c>COM12</c>… (numéro à partir de 1).</summary>
    public static bool IsValid(string? name) => Number(Normalize(name)) is > 0;

    /// <summary>Noms normalisés, sans doublon ni nom invalide, triés par numéro (COM2 avant COM10).</summary>
    public static IReadOnlyList<string> Sort(IEnumerable<string?> names) =>
        [.. names
            .Select(Normalize)
            .Where(IsValid)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => Number(n))];

    private static uint? Number(string name) =>
        name.StartsWith(Prefix, StringComparison.Ordinal)
        && name.Length > Prefix.Length
        && name.Skip(Prefix.Length).All(char.IsAsciiDigit)
        && uint.TryParse(name.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}
