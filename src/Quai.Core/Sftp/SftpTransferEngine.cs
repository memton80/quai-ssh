namespace Quai.Core.Sftp;

/// <summary>
/// Transferts de fichiers et de dossiers entiers au-dessus des opérations élémentaires d'une session SFTP.
/// La progression est un cumul d'octets sur l'ensemble de l'élément transféré.
/// </summary>
public sealed class SftpTransferEngine
{
    private readonly ISftpSession _session;

    public SftpTransferEngine(ISftpSession session)
    {
        _session = session;
    }

    /// <summary>Taille totale d'un fichier ou d'une arborescence locale.</summary>
    public static Task<long> MeasureLocalAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                if (File.Exists(path))
                {
                    return new FileInfo(path).Length;
                }

                long total = 0;
                foreach (var file in EnumerateLocalFiles(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    total += file.Length;
                }

                return total;
            },
            cancellationToken);

    /// <summary>Taille totale d'un fichier ou d'une arborescence distante (les liens vers des dossiers ne sont pas suivis).</summary>
    public async Task<long> MeasureRemoteAsync(RemoteEntry entry, CancellationToken cancellationToken = default)
    {
        if (!entry.IsDirectory)
        {
            return entry.Size;
        }

        long total = 0;
        foreach (var child in await _session.ListAsync(entry.FullPath, cancellationToken).ConfigureAwait(false))
        {
            if (child.IsDirectory && child.IsSymbolicLink)
            {
                continue;
            }

            total += await MeasureRemoteAsync(child, cancellationToken).ConfigureAwait(false);
        }

        return total;
    }

    /// <summary>Envoie un fichier ou un dossier (récursivement) dans <paramref name="remoteDirectory"/>.</summary>
    public async Task UploadAsync(
        string localPath,
        string remoteDirectory,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var counter = new Counter(progress);
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(localPath));
        await UploadCoreAsync(localPath, RemotePath.Combine(remoteDirectory, name), counter, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Récupère un fichier ou un dossier (récursivement) dans <paramref name="localDirectory"/>.</summary>
    public async Task DownloadAsync(
        RemoteEntry entry,
        string localDirectory,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var counter = new Counter(progress);
        await DownloadCoreAsync(entry, Path.Combine(localDirectory, ToLocalName(entry.Name)), counter, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Supprime un fichier, ou un dossier et tout son contenu.</summary>
    public async Task DeleteAsync(RemoteEntry entry, CancellationToken cancellationToken = default)
    {
        // Un lien symbolique est supprimé en tant que lien : on ne touche jamais à sa cible.
        if (entry.IsSymbolicLink)
        {
            await _session.DeleteLinkAsync(entry.FullPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!entry.IsDirectory)
        {
            await _session.DeleteFileAsync(entry.FullPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var child in await _session.ListAsync(entry.FullPath, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeleteAsync(child, cancellationToken).ConfigureAwait(false);
        }

        await _session.DeleteDirectoryAsync(entry.FullPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renomme un fichier, un dossier ou un lien symbolique (sans toucher à la cible du lien).</summary>
    public Task RenameAsync(RemoteEntry entry, string newPath, CancellationToken cancellationToken = default) =>
        entry.IsSymbolicLink
            ? _session.RenameLinkAsync(entry.FullPath, newPath, cancellationToken)
            : _session.RenameAsync(entry.FullPath, newPath, cancellationToken);

    /// <summary>Remplace les caractères interdits dans un nom de fichier local (Windows).</summary>
    internal static string ToLocalName(string remoteName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = string.Concat(remoteName.Select(c => invalid.Contains(c) || c is ':' or '\\' ? '_' : c));
        return name.Length == 0 || name is "." or ".." ? "_" : name;
    }

    private static IEnumerable<FileInfo> EnumerateLocalFiles(string directory) =>
        new DirectoryInfo(directory).EnumerateFiles("*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        });

    private async Task UploadCoreAsync(string localPath, string remotePath, Counter counter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Directory.Exists(localPath))
        {
            if (!await _session.ExistsAsync(remotePath, cancellationToken).ConfigureAwait(false))
            {
                await _session.CreateDirectoryAsync(remotePath, cancellationToken).ConfigureAwait(false);
            }

            var directory = new DirectoryInfo(localPath);
            foreach (var child in directory.EnumerateFileSystemInfos().OrderBy(c => c.Name, StringComparer.Ordinal))
            {
                // Les jonctions et liens ne sont pas suivis (boucles possibles).
                if (child.Attributes.HasFlag(FileAttributes.ReparsePoint) && child is DirectoryInfo)
                {
                    continue;
                }

                await UploadCoreAsync(child.FullName, RemotePath.Combine(remotePath, child.Name), counter, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var file = new FileInfo(localPath);
        var done = false;
        try
        {
            await _session.UploadFileAsync(localPath, remotePath, counter.ForFile(), cancellationToken).ConfigureAwait(false);
            done = true;
        }
        finally
        {
            if (!done)
            {
                // Transfert annulé ou en échec : ne pas laisser un fichier tronqué qui aurait l'air complet.
                await TryAsync(() => _session.DeleteFileAsync(remotePath, CancellationToken.None)).ConfigureAwait(false);
            }
        }

        counter.CompleteFile(file.Length);
        await TryAsync(() => _session.SetLastWriteTimeAsync(remotePath, file.LastWriteTimeUtc, cancellationToken)).ConfigureAwait(false);
    }

    private async Task DownloadCoreAsync(RemoteEntry entry, string localPath, Counter counter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (entry.IsDirectory)
        {
            Directory.CreateDirectory(localPath);
            foreach (var child in await _session.ListAsync(entry.FullPath, cancellationToken).ConfigureAwait(false))
            {
                if (child.IsDirectory && child.IsSymbolicLink)
                {
                    continue;
                }

                await DownloadCoreAsync(child, Path.Combine(localPath, ToLocalName(child.Name)), counter, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var done = false;
        try
        {
            await _session.DownloadFileAsync(entry.FullPath, localPath, counter.ForFile(), cancellationToken).ConfigureAwait(false);
            done = true;
        }
        finally
        {
            if (!done)
            {
                try
                {
                    File.Delete(localPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Le fichier partiel reste en place.
                }
            }
        }

        counter.CompleteFile(entry.Size);
        try
        {
            File.SetLastWriteTime(localPath, entry.LastWriteTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            // La date de modification est un confort, pas une obligation.
        }
    }

    private static async Task TryAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Opération de confort ou de nettoyage : son échec ne change pas le résultat du transfert.
        }
    }

    /// <summary>Additionne la progression des fichiers successifs d'un même transfert.</summary>
    private sealed class Counter(IProgress<long>? progress)
    {
        private long _completed;

        public IProgress<long>? ForFile() => progress is null ? null : new FileProgress(this);

        public void CompleteFile(long size)
        {
            _completed += size;
            progress?.Report(_completed);
        }

        private sealed class FileProgress(Counter owner) : IProgress<long>
        {
            public void Report(long value) => owner.Report(value);
        }

        private void Report(long fileBytes) => progress?.Report(_completed + fileBytes);
    }
}
