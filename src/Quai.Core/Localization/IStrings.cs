namespace Quai.Core.Localization;

/// <summary>Chaînes localisées de l'interface (fournies par les ressources <c>.resw</c> de l'application).</summary>
public interface IStrings
{
    string Get(string key);

    string Format(string key, params object?[] args);
}
