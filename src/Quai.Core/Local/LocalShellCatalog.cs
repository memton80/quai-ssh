using Microsoft.Win32;
using Quai.Core.Localization;
using Quai.Core.Models;

namespace Quai.Core.Local;

/// <summary>Shells installés sur cet ordinateur, proposés pour un terminal local.</summary>
public interface ILocalShellCatalog
{
    /// <summary>Shells présents, dans l'ordre de préférence (le premier est le shell par défaut).</summary>
    IReadOnlyList<LocalShell> GetShells();
}

/// <summary>Ce dont la détection a besoin du système : remplacé dans les tests.</summary>
public sealed class LocalShellEnvironment
{
    public required Func<string, bool> FileExists { get; init; }

    public required Func<string, string?> GetVariable { get; init; }

    /// <summary>Dossier système (<c>C:\Windows\System32</c>).</summary>
    public required string SystemDirectory { get; init; }

    /// <summary>Distributions WSL installées (noms), dans l'ordre du registre.</summary>
    public required Func<IReadOnlyList<string>> WslDistributions { get; init; }

    public bool IsWindows { get; init; } = true;

    public static LocalShellEnvironment Current { get; } = new()
    {
        FileExists = File.Exists,
        GetVariable = Environment.GetEnvironmentVariable,
        SystemDirectory = Environment.SystemDirectory,
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
