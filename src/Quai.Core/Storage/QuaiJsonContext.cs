using System.Text.Json;
using System.Text.Json.Serialization;
using Quai.Core.Models;

namespace Quai.Core.Storage;

/// <summary>
/// Sérialisation des fichiers JSON de Quai, générée à la compilation : aucune réflexion, ce qui permet de réduire
/// l'application à la publication (trimming / AOT). Même format que l'ancien sérialiseur par réflexion (vérifié
/// par les tests) : propriétés et énumérations en camelCase, indentation, commentaires et virgules finales tolérés.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    Converters =
    [
        typeof(CamelCaseEnumConverter<AppTheme>),
        typeof(CamelCaseEnumConverter<ConnectionProtocol>),
        typeof(CamelCaseEnumConverter<AuthenticationKind>),
        typeof(CamelCaseEnumConverter<SerialParity>),
        typeof(CamelCaseEnumConverter<SerialStopBits>),
        typeof(CamelCaseEnumConverter<SerialFlowControl>),
    ])]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ConnectionDocument))]
[JsonSerializable(typeof(SnippetDocument))]
internal sealed partial class QuaiJsonContext : JsonSerializerContext;

/// <summary>Énumération écrite en texte camelCase (<c>"privateKey"</c>) ; les nombres restent acceptés en lecture.</summary>
internal sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase)
    where TEnum : struct, Enum;
