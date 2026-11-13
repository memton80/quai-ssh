using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quai.Core.Storage;

/// <summary>Lecture / écriture JSON avec remplacement atomique du fichier.</summary>
internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static T? Read<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options);
    }

    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Écriture dans un fichier temporaire puis remplacement : pas de fichier à moitié écrit en cas de coupure.
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, value, Options);
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Met de côté un fichier illisible pour ne pas l'écraser.</summary>
    public static string? Quarantine(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var target = $"{path}.corrompu-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Move(path, target, overwrite: true);
        return target;
    }
}
