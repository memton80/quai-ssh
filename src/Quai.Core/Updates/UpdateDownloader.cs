using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Quai.Core.Updates;

/// <summary>Le fichier reçu ne correspond pas à celui annoncé par GitHub (taille ou empreinte) : il est supprimé.</summary>
public sealed class UpdateIntegrityException(string message) : Exception(message);

/// <summary>
/// Téléchargement de l'installateur d'une mise à jour, avec progression et annulation. Le fichier est écrit à côté
/// (<c>.partial</c>) et ne prend son vrai nom qu'une fois sa taille et son empreinte SHA-256 vérifiées : un fichier
/// incomplet ou altéré n'est jamais exécuté, et il est supprimé.
/// </summary>
public sealed class UpdateDownloader : IDisposable
{
    private const int BufferSize = 81920;
    private const string PartialSuffix = ".partial";

    private readonly HttpClient _http;

    public UpdateDownloader(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);

        // Un installateur de ~35 Mo sur une connexion lente : la lecture n'est pas bornée, l'utilisateur peut annuler.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.Name, AppInfo.Version));
    }

    /// <param name="progress">Avancement de 0 à 1, signalé à chaque point de pourcentage.</param>
    /// <returns>Chemin de l'installateur vérifié.</returns>
    public async Task<string> DownloadAsync(UpdatePackage package, string directory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.DownloadUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Une mise à jour ne se télécharge qu'en HTTPS.", nameof(package));
        }

        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Path.GetFileName(package.FileName));
        if (File.Exists(target) && await MatchesAsync(target, package, cancellationToken).ConfigureAwait(false))
        {
            // Déjà téléchargé (mise à jour reportée puis relancée).
            progress?.Report(1);
            return target;
        }

        var partial = target + PartialSuffix;
        try
        {
            using var response = await _http.GetAsync(package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != package.Size)
            {
                throw new UpdateIntegrityException($"Taille annoncée {length} au lieu de {package.Size} octets.");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            var reported = -1;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > package.Size)
                    {
                        throw new UpdateIntegrityException($"Plus de {package.Size} octets reçus.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

                    var percent = (int)(total * 100 / package.Size);
                    if (percent != reported)
                    {
                        reported = percent;
                        progress?.Report(total / (double)package.Size);
                    }
                }
            }

            if (total != package.Size)
            {
                throw new UpdateIntegrityException($"{total} octets reçus au lieu de {package.Size}.");
            }

            if (!Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateIntegrityException("Empreinte SHA-256 différente de celle publiée.");
            }

            File.Move(partial, target, overwrite: true);
            return target;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    /// <summary>Supprime les installateurs des mises à jour précédentes (ceux encore utilisés sont laissés).</summary>
    public static void CleanUp(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            TryDelete(file);
        }
    }

    public void Dispose() => _http.Dispose();

    private static async Task<bool> MatchesAsync(string path, UpdatePackage package, CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(path).Length != package.Size)
            {
                return false;
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(digest).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Encore ouvert (installateur en cours d'exécution) : il partira au prochain nettoyage.
        }
    }
}
