using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Quai.Core.Storage;

/// <summary>Lecture / écriture JSON avec remplacement atomique du fichier (format : <see cref="QuaiJsonContext"/>).</summary>
internal static class JsonFile
{
    public static T? Read<T>(string path, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, typeInfo);
    }

    public static void Write<T>(string path, T value, JsonTypeInfo<T> typeInfo)
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
            JsonSerializer.Serialize(stream, value, typeInfo);
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
