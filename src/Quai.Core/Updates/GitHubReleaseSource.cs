using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Quai.Core.Updates;

/// <summary>Fichier joint à une release (installateur, zip).</summary>
/// <param name="Sha256">Empreinte SHA-256 calculée par GitHub à la publication (hexadécimal minuscule), si connue.</param>
public sealed record ReleaseAsset(string Name, long Size, string? Sha256);

/// <summary>Dernière version publiée et ses fichiers.</summary>
public sealed record ReleaseInfo(string Tag, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Où lire la dernière version publiée.</summary>
public interface IReleaseSource
{
    /// <summary>Dernière version publiée (« v1.2.0 » et ses fichiers), ou <c>null</c> s'il n'y en a aucune.</summary>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Dernière release GitHub du dépôt de Quai (API publique, sans compte ni jeton). Seule information
/// envoyée : la requête elle-même, avec « Quai/version » comme agent ; rien sur l'utilisateur ou ses connexions.
/// Les brouillons et préversions ne sont jamais proposés (point d'accès <c>releases/latest</c>).
/// </summary>
public sealed class GitHubReleaseSource : IReleaseSource, IDisposable
{
    public const string Repository = "memton80/quai-ssh";

    private const string DigestPrefix = "sha256:";

    private static readonly Uri LatestRelease = new($"https://api.github.com/repos/{Repository}/releases/latest");

    private readonly HttpClient _http;

    public GitHubReleaseSource(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.Name, AppInfo.Version));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    /// <summary>Page de la release, construite ici (jamais une adresse reçue du réseau).</summary>
    public static Uri ReleasePage(string tag) =>
        new($"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(tag)}");

    /// <summary>Adresse de téléchargement d'un fichier de la release, construite ici (jamais reçue du réseau).</summary>
    public static Uri DownloadUrl(string tag, string fileName) =>
        new($"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(fileName)}");

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(LatestRelease, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Aucune release publiée pour l'instant.
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("tag_name", out var tag)
            || tag.ValueKind != JsonValueKind.String
            || tag.GetString() is not { } tagName)
        {
            return null;
        }

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in list.EnumerateArray())
            {
                if (ReadAsset(asset) is { } parsed)
                {
                    assets.Add(parsed);
                }
            }
        }

        return new ReleaseInfo(tagName, assets);
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Nom, taille et empreinte (<c>"digest": "sha256:…"</c>) ; un fichier mal décrit est ignoré.</summary>
    private static ReleaseAsset? ReadAsset(JsonElement asset)
    {
        if (asset.ValueKind != JsonValueKind.Object
            || !asset.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
            || !asset.TryGetProperty("size", out var size) || !size.TryGetInt64(out var bytes))
        {
            return null;
        }

        string? sha256 = null;
        if (asset.TryGetProperty("digest", out var digest)
            && digest.ValueKind == JsonValueKind.String
            && digest.GetString() is { } value
            && value.StartsWith(DigestPrefix, StringComparison.OrdinalIgnoreCase)
            && IsSha256(value.AsSpan(DigestPrefix.Length)))
        {
            sha256 = value[DigestPrefix.Length..].ToLowerInvariant();
        }

        return new ReleaseAsset(name.GetString()!, bytes, sha256);
    }

    private static bool IsSha256(ReadOnlySpan<char> hex)
    {
        if (hex.Length != 64)
        {
            return false;
        }

        foreach (var c in hex)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
