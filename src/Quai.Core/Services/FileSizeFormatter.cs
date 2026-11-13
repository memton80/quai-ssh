using System.Globalization;
using Quai.Core.Localization;

namespace Quai.Core.Services;

/// <summary>Tailles lisibles : « 1,5 Mo » (unités localisées, base 1024).</summary>
public sealed class FileSizeFormatter
{
    private readonly string[] _units;

    public FileSizeFormatter(IStrings strings)
    {
        var units = strings.Get("Size_Units").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        _units = units.Length >= 5 ? units : ["o", "Ko", "Mo", "Go", "To"];
    }

    public string Format(long bytes)
    {
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < _units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 || value >= 100 ? "0" : "0.#";
        return $"{value.ToString(format, CultureInfo.CurrentCulture)} {_units[unit]}";
    }
}
