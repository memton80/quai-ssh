using Quai.Core.Storage;

namespace Quai.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void Explicit_root_wins_and_files_live_under_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "quai-tests-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);

        Assert.Equal(root, paths.Root);
        Assert.StartsWith(root, paths.ConnectionsFile);
        Assert.StartsWith(root, paths.SettingsFile);
        Assert.StartsWith(root, paths.KnownHostsFile);
        Assert.StartsWith(root, paths.LogsDirectory);
    }

    [Fact]
    public void Default_root_is_named_after_the_app()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable)))
        {
            Assert.Skip("QUAI_DATA_DIR est défini dans cet environnement.");
        }

        Assert.EndsWith(AppInfo.Name, new AppPaths().Root);
    }

    [Fact]
    public void EnsureCreated_creates_the_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), "quai-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCreated();
            Assert.True(Directory.Exists(paths.Root));
            Assert.True(Directory.Exists(paths.LogsDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
