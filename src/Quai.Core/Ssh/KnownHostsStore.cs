using System.Globalization;
using System.Text;
using Quai.Core.Storage;

namespace Quai.Core.Ssh;

/// <summary>Hôtes dont la clé a été acceptée par l'utilisateur.</summary>
public interface IKnownHostsStore
{
    /// <summary>Clés enregistrées pour cet hôte et ce port.</summary>
    IReadOnlyList<HostKeyInfo> Find(string host, int port);

    HostKeyStatus Check(HostKeyInfo presented);

    /// <summary>Enregistre la clé comme seule clé de confiance de cet hôte (remplace les précédentes).</summary>
    void Trust(HostKeyInfo key);

    bool Forget(string host, int port);
}

/// <summary>
/// Fichier <c>known_hosts</c> propre à l'application, au format OpenSSH non haché :
/// <c>hôte type clé-base64</c>, ou <c>[hôte]:port type clé-base64</c> si le port n'est pas 22.
/// </summary>
public sealed class KnownHostsStore : IKnownHostsStore
{
    private const int DefaultPort = 22;
    private readonly string _path;
    private readonly Lock _gate = new();

    public KnownHostsStore(AppPaths paths)
        : this(paths.KnownHostsFile)
    {
    }

    public KnownHostsStore(string path)
    {
        _path = path;
    }

    public IReadOnlyList<HostKeyInfo> Find(string host, int port)
    {
        lock (_gate)
        {
            return [.. Load().Where(e => IsSameHost(e, host, port))];
        }
    }

    public HostKeyStatus Check(HostKeyInfo presented)
    {
        var known = Find(presented.Host, presented.Port);
        if (known.Count == 0)
        {
            return HostKeyStatus.Unknown;
        }

        return known.Any(k => k.KeyType == presented.KeyType && k.Key.AsSpan().SequenceEqual(presented.Key))
            ? HostKeyStatus.Trusted
            : HostKeyStatus.Changed;
    }

    public void Trust(HostKeyInfo key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            var entries = Load();
            entries.RemoveAll(e => IsSameHost(e, key.Host, key.Port));
            entries.Add(key with { Host = Normalize(key.Host) });
            Save(entries);
        }
    }

    public bool Forget(string host, int port)
    {
        lock (_gate)
        {
            var entries = Load();
            if (entries.RemoveAll(e => IsSameHost(e, host, port)) == 0)
            {
                return false;
            }

            Save(entries);
            return true;
        }
    }

    internal static string FormatLine(HostKeyInfo entry)
    {
        var host = entry.Port == DefaultPort
            ? entry.Host
            : string.Create(CultureInfo.InvariantCulture, $"[{entry.Host}]:{entry.Port}");
        return $"{host} {entry.KeyType} {Convert.ToBase64String(entry.Key)}";
    }

    internal static IEnumerable<HostKeyInfo> ParseLine(string line)
    {
        var text = line.Trim();

        // Commentaires, marqueurs (@cert-authority, @revoked) et entrées hachées (|1|…) sont ignorés.
        if (text.Length == 0 || text[0] is '#' or '@' or '|')
        {
            yield break;
        }

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            yield break;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            yield break;
        }

        foreach (var pattern in parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var host = pattern;
            var port = DefaultPort;
            if (pattern.StartsWith('['))
            {
                var end = pattern.IndexOf("]:", StringComparison.Ordinal);
                if (end < 0 || !int.TryParse(pattern[(end + 2)..], NumberStyles.None, CultureInfo.InvariantCulture, out port))
                {
                    continue;
                }

                host = pattern[1..end];
            }

            yield return new HostKeyInfo(Normalize(host), port, parts[1], key);
        }
    }

    private static string Normalize(string host) => host.Trim().ToLowerInvariant();

    private static bool IsSameHost(HostKeyInfo entry, string host, int port) =>
        entry.Port == port && entry.Host == Normalize(host);

    private List<HostKeyInfo> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        return [.. File.ReadAllLines(_path).SelectMany(ParseLine)];
    }

    private void Save(List<HostKeyInfo> entries)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.AppendLine("# Hôtes SSH connus de Quai (fichier géré par l'application).");
        foreach (var entry in entries.OrderBy(e => e.Host, StringComparer.Ordinal).ThenBy(e => e.Port))
        {
            builder.AppendLine(FormatLine(entry));
        }

        var temp = _path + ".tmp";
        File.WriteAllText(temp, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, _path, overwrite: true);
    }
}
