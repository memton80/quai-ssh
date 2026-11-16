using System.Net;
using System.Security.Cryptography;
using Quai.Core.Updates;

namespace Quai.App.Services;

/// <summary>
/// Test de la mise à jour en place (CI) : l'installateur que le CI vient de construire est présenté comme la release
/// <see cref="SmokeSessionFactory.ReleaseTag"/>, avec sa vraie taille et sa vraie empreinte, et servi depuis le disque
/// à la place de GitHub. Tout le reste est le parcours réel : vérification, installation silencieuse, relance.
/// </summary>
internal static class SmokeUpdate
{
    public static (IReleaseSource Source, UpdateDownloader Downloader) Create(string installerPath, string? installerName)
    {
        if (installerName is null)
        {
            throw new PlatformNotSupportedException("Aucun installateur publié pour cette architecture.");
        }

        var size = new FileInfo(installerPath).Length;
        string sha256;
        using (var stream = File.OpenRead(installerPath))
        {
            sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        var release = new ReleaseInfo(SmokeSessionFactory.ReleaseTag, [new ReleaseAsset(installerName, size, sha256)]);
        var url = GitHubReleaseSource.DownloadUrl(SmokeSessionFactory.ReleaseTag, installerName);
        return (new FixedSource(release), new UpdateDownloader(new LocalFileHandler(url, installerPath)));
    }

    private sealed class FixedSource(ReleaseInfo release) : IReleaseSource
    {
        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default) => Task.FromResult<ReleaseInfo?>(release);
    }

    /// <summary>Répond pour la seule adresse attendue (sinon 404), comme le ferait GitHub.</summary>
    private sealed class LocalFileHandler(Uri url, string path) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri != url)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
            }

            var content = new StreamContent(new PacedStream(File.OpenRead(path)));
            content.Headers.ContentLength = new FileInfo(path).Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }

    /// <summary>Lecture ralentie (environ 3 s pour l'installateur) : la barre de progression a le temps d'être capturée.</summary>
    private sealed class PacedStream(Stream inner) : Stream
    {
        private const int Step = 256 * 1024;
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            if ((_read / Step) != ((_read + read) / Step))
            {
                await Task.Delay(20, cancellationToken);
            }

            _read += read;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
