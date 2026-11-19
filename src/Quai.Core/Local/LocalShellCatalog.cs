using Microsoft.Win32;
using Quai.Core.Localization;
using Quai.Core.Models;

namespace Quai.Core.Local;

/// <summary>Shells installés sur cet ordinateur, proposés pour un terminal local.</summary>
public interface ILocalShellCatalog
{
    /// <summary>Shells présents, dans l'ordre de préférence (le premier est le shell par défaut).</summary>
    IReadOnlyList<LocalShell> GetShells();

    /// <summary>
    /// Shells proposés en administrateur : uniquement des exécutables de dossiers protégés par Windows, sans script de
    /// démarrage modifiable sans droits (voir <see cref="LocalShellCatalog.GetAdminShells"/>).
    /// </summary>
    IReadOnlyList<LocalShell> GetAdminShells() => [];
}

/// <summary>Ce dont la détection a besoin du système : remplacé dans les tests.</summary>
public sealed class LocalShellEnvironment
{
    public required Func<string, bool> FileExists { get; init; }

    public required Func<string, string?> GetVariable { get; init; }

    /// <summary>Dossier système (<c>C:\Windows\System32</c>).</summary>
    public required string SystemDirectory { get; init; }

    /// <summary>
    /// Dossier « Program Files » de Windows, lu comme dossier connu (registre de la machine) et non dans une variable
    /// d'environnement : il sert à choisir ce qui sera lancé en administrateur.
    /// </summary>
    public string? ProgramFilesDirectory { get; init; }

    /// <summary>Distributions WSL installées (noms), dans l'ordre du registre.</summary>
    public required Func<IReadOnlyList<string>> WslDistributions { get; init; }

    public bool IsWindows { get; init; } = true;

    public static LocalShellEnvironment Current { get; } = new()
    {
        FileExists = File.Exists,
        GetVariable = Environment.GetEnvironmentVariable,
        SystemDirectory = Environment.SystemDirectory,
        ProgramFilesDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        WslDistributions = ReadWslDistributions,
        IsWindows = OperatingSystem.IsWindows(),
    };

    /// <summary>
    /// Distributions WSL de l'utilisateur, lues dans le registre comme le fait Windows Terminal (lancer
    /// <c>wsl.exe --list</c> réveillerait la machine virtuelle). Les distributions internes de Docker Desktop sont écartées.
    /// </summary>
    private static IReadOnlyList<string> ReadWslDistributions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            using var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
            if (lxss is null)
            {
                return [];
            }

            var names = new List<string>();
            foreach (var id in lxss.GetSubKeyNames())
            {
                using var distribution = lxss.OpenSubKey(id);
                if (distribution?.GetValue("DistributionName") is string name
                    && !string.IsNullOrWhiteSpace(name)
                    && !name.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }

            return names;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}

/// <summary>
/// Recherche des shells habituels : PowerShell 7, Windows PowerShell, invite de commandes, distributions WSL et
/// Git Bash. Rien n'est lancé pour les détecter : seuls des fichiers et le registre sont lus.
/// </summary>
public sealed class LocalShellCatalog : ILocalShellCatalog
{
    public const string PowerShellId = "pwsh";
    public const string WindowsPowerShellId = "powershell";
    public const string CommandPromptId = "cmd";
    public const string GitBashId = "gitbash";
    public const string WslPrefix = "wsl:";

    /// <summary>Préfixe des identifiants des shells administrateur (jamais mémorisés comme shell par défaut).</summary>
    public const string AdminPrefix = "admin:";

    private readonly LocalShellEnvironment _environment;
    private readonly IStrings? _strings;

    public LocalShellCatalog(IStrings strings)
        : this(LocalShellEnvironment.Current, strings)
    {
    }

    public LocalShellCatalog(LocalShellEnvironment environment, IStrings? strings = null)
    {
        _environment = environment;
        _strings = strings;
    }

    /// <summary>Le shell <paramref name="id"/> s'il est présent, sinon le premier détecté (<c>null</c> si aucun).</summary>
    public static LocalShell? Resolve(IReadOnlyList<LocalShell> shells, string? id) =>
        shells.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) ?? shells.FirstOrDefault();

    public IReadOnlyList<LocalShell> GetShells()
    {
        if (!_environment.IsWindows)
        {
            // Développement et tests hors Windows : le shell de l'utilisateur.
            var shell = _environment.GetVariable("SHELL") is { Length: > 0 } path && _environment.FileExists(path) ? path : "/bin/sh";
            return [new LocalShell("sh", Path.GetFileName(shell), shell, [])];
        }

        var shells = new List<LocalShell>();
        var system = _environment.SystemDirectory;

        if (FindPowerShell7() is { } pwsh)
        {
            shells.Add(new LocalShell(PowerShellId, "PowerShell", pwsh, ["-NoLogo"]));
        }

        var windowsPowerShell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (_environment.FileExists(windowsPowerShell))
        {
            shells.Add(new LocalShell(WindowsPowerShellId, "Windows PowerShell", windowsPowerShell, ["-NoLogo"]));
        }

        var cmd = Path.Combine(system, "cmd.exe");
        if (_environment.FileExists(cmd))
        {
            shells.Add(new LocalShell(CommandPromptId, _strings?.Get("LocalShell_CommandPrompt") ?? "cmd", cmd, []));
        }

        var wsl = Path.Combine(system, "wsl.exe");
        if (_environment.FileExists(wsl))
        {
            foreach (var distribution in _environment.WslDistributions())
            {
                shells.Add(new LocalShell(WslPrefix + distribution, distribution, wsl, ["-d", distribution, "--cd", "~"]));
            }
        }

        if (FindGitBash() is { } bash)
        {
            shells.Add(new LocalShell(GitBashId, "Git Bash", bash, ["--login", "-i"]));
        }

        return shells;
    }

    /// <summary>
    /// Shells lancés en administrateur, dans leur propre fenêtre, par Windows (UAC) : PowerShell 7 de « Program Files »,
    /// Windows PowerShell et l'invite de commandes de System32 — des dossiers où seul un administrateur peut écrire.
    /// Écartés : tout ce qui vient du PATH ou d'un dossier de l'utilisateur (un programme sans droits pourrait y déposer
    /// un faux shell et obtenir les droits d'administrateur à la confirmation suivante), WSL et Git Bash. Aucun script
    /// de démarrage modifiable sans droits n'est exécuté : profils PowerShell (<c>-NoProfile</c>) et AutoRun de
    /// l'invite de commandes (<c>/D</c>).
    /// </summary>
    public IReadOnlyList<LocalShell> GetAdminShells()
    {
        if (!_environment.IsWindows)
        {
            return [];
        }

        var shells = new List<LocalShell>();
        if (_environment.ProgramFilesDirectory is { } programFiles && IsLocalDrivePath(programFiles))
        {
            var pwsh = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
            if (_environment.FileExists(pwsh))
            {
                shells.Add(Admin(PowerShellId, "PowerShell", pwsh, ["-NoLogo", "-NoProfile"]));
            }
        }

        var system = _environment.SystemDirectory;
        if (!IsLocalDrivePath(system))
        {
            return shells;
        }

        var windowsPowerShell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (_environment.FileExists(windowsPowerShell))
        {
            shells.Add(Admin(WindowsPowerShellId, "Windows PowerShell", windowsPowerShell, ["-NoLogo", "-NoProfile"]));
        }

        var cmd = Path.Combine(system, "cmd.exe");
        if (_environment.FileExists(cmd))
        {
            shells.Add(Admin(CommandPromptId, _strings?.Get("LocalShell_CommandPrompt") ?? "cmd", cmd, ["/D"]));
        }

        return shells;
    }

    /// <summary>
    /// Chemin absolu sur un lecteur local (<c>C:\…</c>) : ni relatif (résolu ailleurs que prévu), ni réseau
    /// (<c>\\serveur\…</c>), seules formes acceptées pour un exécutable lancé en administrateur.
    /// </summary>
    public static bool IsLocalDrivePath(string? path) =>
        path is { Length: >= 3 } && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\' && !path.Contains("..", StringComparison.Ordinal);

    private static LocalShell Admin(string id, string name, string path, IReadOnlyList<string> arguments) =>
        new(AdminPrefix + id, name, path, arguments);

    /// <summary>PowerShell 7 : installation classique (MSI, winget) puis tout <c>pwsh.exe</c> du PATH (Microsoft Store).</summary>
    private string? FindPowerShell7()
    {
        foreach (var root in ProgramFilesFolders())
        {
            var path = Path.Combine(root, "PowerShell", "7", "pwsh.exe");
            if (_environment.FileExists(path))
            {
                return path;
            }
        }

        return FindOnPath("pwsh.exe");
    }

    private string? FindGitBash()
    {
        var roots = ProgramFilesFolders().ToList();
        if (_environment.GetVariable("LOCALAPPDATA") is { Length: > 0 } local)
        {
            roots.Add(Path.Combine(local, "Programs"));
        }

        return roots
            .Select(root => Path.Combine(root, "Git", "bin", "bash.exe"))
            .FirstOrDefault(_environment.FileExists);
    }

    private IEnumerable<string> ProgramFilesFolders() =>
        new[] { "ProgramW6432", "ProgramFiles", "ProgramFiles(x86)" }
            .Select(_environment.GetVariable)
            .OfType<string>()
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private string? FindOnPath(string file)
    {
        var path = _environment.GetVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(_environment.IsWindows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory.Trim('"'), file);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (_environment.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
