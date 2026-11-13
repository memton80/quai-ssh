using Microsoft.Windows.ApplicationModel.Resources;

namespace Quai.App.Localization;

/// <summary>Accès aux chaînes localisées (<c>Strings/&lt;langue&gt;/Resources.resw</c>).</summary>
public static class Loc
{
    private static readonly Lazy<ResourceLoader?> Loader = new(CreateLoader);

    /// <summary>Retourne la chaîne associée à <paramref name="key"/>, ou la clé elle-même si elle est introuvable.</summary>
    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        try
        {
            var value = Loader.Value?.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception)
        {
            return key;
        }
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), args);

    private static ResourceLoader? CreateLoader()
    {
        try
        {
            return new ResourceLoader();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
