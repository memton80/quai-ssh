namespace Quai.Core.Storage;

/// <summary>
/// Emplacements des fichiers de l'application (par défaut <c>%LOCALAPPDATA%\Quai</c>).
/// La variable d'environnement <c>QUAI_DATA_DIR</c> permet de rediriger le tout (tests, CI).
/// </summary>
public sealed class AppPaths
{
    public const string DataDirectoryVariable = "QUAI_DATA_DIR";

    public AppPaths(string? root = null)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        Root = !string.IsNullOrWhiteSpace(root)
            ? root
            : !string.IsNullOrWhiteSpace(fromEnvironment)
                ? fromEnvironment
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name);
    }

    public string Root { get; }

    public string ConnectionsFile => Path.Combine(Root, "connections.json");

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string KnownHostsFile => Path.Combine(Root, "known_hosts");

    public string LogsDirectory => Path.Combine(Root, "logs");

    public string WebViewDataDirectory => Path.Combine(Root, "WebView2");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
    }
}
