using System.Runtime.InteropServices;
using Semver;

namespace Quai.Core.Updates;

/// <summary>Installateur à télécharger : adresse construite par l'application, taille et empreinte annoncées par GitHub.</summary>
public sealed record UpdatePackage(string FileName, Uri DownloadUrl, long Size, string Sha256);

/// <summary>Version plus récente que celle en cours d'exécution.</summary>
/// <param name="Installer">Installateur pour cette architecture, ou <c>null</c> s'il manque (ou sans empreinte) : la page de la release reste proposée.</param>
public sealed record AvailableUpdate(string Version, Uri ReleasePage, UpdatePackage? Installer = null);

/// <summary>Compare la dernière release publiée à la version en cours (règles SemVer : 1.2.0-ci.4 &lt; 1.2.0).</summary>
public sealed class UpdateChecker
{
    /// <summary>Au-delà, le fichier annoncé n'est pas un installateur de Quai (~35 Mo) : il n'est pas téléchargé.</summary>
    public const long MaxInstallerSize = 512L * 1024 * 1024;

    private readonly IReleaseSource _source;
    private readonly string _currentVersion;
    private readonly string? _installerName;

    /// <param name="installerName">Nom de l'installateur de cette architecture dans la release (<see cref="InstallerNameFor"/>).</param>
    public UpdateChecker(IReleaseSource source, string currentVersion, string? installerName = null)
    {
        _source = source;
        _currentVersion = currentVersion;
        _installerName = installerName;
    }

    /// <summary>Installateur publié par le CI pour cette architecture (<c>Quai-Setup-x64.exe</c>, <c>Quai-Setup-arm64.exe</c>).</summary>
    public static string? InstallerNameFor(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "Quai-Setup-x64.exe",
        Architecture.Arm64 => "Quai-Setup-arm64.exe",
        _ => null,
    };

    /// <returns>La mise à jour disponible, ou <c>null</c> si la version en cours est à jour.</returns>
    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var release = await _source.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        var tag = release?.Tag.Trim();
        if (release is null
            || string.IsNullOrEmpty(tag)
            || !SemVersion.TryParse(tag, SemVersionStyles.AllowV, out var latest)
            || !SemVersion.TryParse(_currentVersion, SemVersionStyles.AllowV, out var current)
            || latest.ComparePrecedenceTo(current) <= 0)
        {
            return null;
        }

        return new AvailableUpdate(latest.ToString(), GitHubReleaseSource.ReleasePage(tag), FindInstaller(release, tag));
    }

    /// <summary>
    /// L'installateur de cette architecture, seulement s'il a une taille plausible et une empreinte SHA-256 :
    /// sans empreinte, rien ne permettrait de vérifier le fichier reçu avant de l'exécuter.
    /// </summary>
    private UpdatePackage? FindInstaller(ReleaseInfo release, string tag)
    {
        if (_installerName is null)
        {
            return null;
        }

        var asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, _installerName, StringComparison.Ordinal));
        if (asset is not { Sha256: { } sha256, Size: > 0 and <= MaxInstallerSize })
        {
            return null;
        }

        return new UpdatePackage(asset.Name, GitHubReleaseSource.DownloadUrl(tag, asset.Name), asset.Size, sha256);
    }
}
