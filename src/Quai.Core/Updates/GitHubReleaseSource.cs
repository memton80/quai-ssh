using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Quai.Core.Updates;

/// <summary>Où lire le numéro de la dernière version publiée.</summary>
public interface IReleaseSource
{
    /// <summary>Étiquette de la dernière version publiée (« v1.2.0 »), ou <c>null</c> s'il n'y en a aucune.</summary>
    Task<string?> GetLatestTagAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Dernière release GitHub du dépôt de Quai (API publique, sans compte ni jeton). Seule information
/// envoyée : la requête elle-même, avec « Quai/version » comme agent ; rien sur l'utilisateur ou ses connexions.
/// Les brouillons et préversions ne sont jamais proposés (point d'accès <c>releases/latest</c>).
/// </summary>
public sealed class GitHubReleaseSource : IReleaseSource, IDisposable
{
    public const string Repository = "memton80/quai-ssh";

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

    public async Task<string?> GetLatestTagAsync(CancellationToken cancellationToken = default)
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
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("tag_name", out var tag)
            && tag.ValueKind == JsonValueKind.String
                ? tag.GetString()
                : null;
    }

    public void Dispose() => _http.Dispose();
}
