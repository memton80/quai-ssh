using System.Reflection;

namespace Quai.Core;

/// <summary>Constantes d'identité de l'application.</summary>
public static class AppInfo
{
    /// <summary>
    /// Nom technique, figé : dossier <c>%LOCALAPPDATA%\Quai</c>, ressource du coffre Windows, exécutable.
    /// Le changer ferait perdre leurs données aux installations existantes.
    /// </summary>
    public const string Name = "Quai";

    /// <summary>Nom affiché à l'utilisateur.</summary>
    public const string DisplayName = "Quai SSH";

    /// <summary>Version lisible (« 1.0.0 »), sans le suffixe de commit ajouté par le SDK.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plus = informational.IndexOf('+');
        return plus > 0 ? informational[..plus] : informational;
    }
}
