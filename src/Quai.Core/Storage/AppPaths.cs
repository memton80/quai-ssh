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

    public string SnippetsFile => Path.Combine(Root, "snippets.json");

    public string KnownHostsFile => Path.Combine(Root, "known_hosts");

    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>Copies locales des fichiers distants ouverts pour modification (effacées à la fermeture de l'onglet SFTP).</summary>
    public string EditsDirectory => Path.Combine(Root, "edits");

    /// <summary>Installateur de la mise à jour en cours (effacé au démarrage suivant).</summary>
    public string UpdatesDirectory => Path.Combine(Root, "updates");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
    }
}
