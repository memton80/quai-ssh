using Semver;

namespace Quai.Core.Updates;

/// <summary>Version plus récente que celle en cours d'exécution.</summary>
public sealed record AvailableUpdate(string Version, Uri ReleasePage);

/// <summary>Compare la dernière release publiée à la version en cours (règles SemVer : 1.2.0-ci.4 &lt; 1.2.0).</summary>
public sealed class UpdateChecker
{
    private readonly IReleaseSource _source;
    private readonly string _currentVersion;

    public UpdateChecker(IReleaseSource source, string currentVersion)
    {
        _source = source;
        _currentVersion = currentVersion;
    }

    /// <returns>La mise à jour disponible, ou <c>null</c> si la version en cours est à jour.</returns>
    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var tag = await _source.GetLatestTagAsync(cancellationToken).ConfigureAwait(false);
        if (tag is null
            || !SemVersion.TryParse(tag.Trim(), SemVersionStyles.AllowV, out var latest)
            || !SemVersion.TryParse(_currentVersion, SemVersionStyles.AllowV, out var current)
            || latest.ComparePrecedenceTo(current) <= 0)
        {
            return null;
        }

        return new AvailableUpdate(latest.ToString(), GitHubReleaseSource.ReleasePage(tag.Trim()));
    }
}
