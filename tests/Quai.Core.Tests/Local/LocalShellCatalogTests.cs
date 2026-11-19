using Quai.Core.Local;
using Quai.Core.Tests.Support;

namespace Quai.Core.Tests.Local;

public sealed class LocalShellCatalogTests
{
    private const string System32 = @"C:\Windows\System32";
    private const string ProgramFiles = @"C:\Program Files";

    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProgramFiles"] = ProgramFiles,
        ["ProgramW6432"] = ProgramFiles,
    };

    private readonly List<string> _distributions = [];

    private LocalShellCatalog Catalog() => new(
        new LocalShellEnvironment
        {
            FileExists = _files.Contains,
            GetVariable = name => _variables.GetValueOrDefault(name),
            SystemDirectory = System32,
            WslDistributions = () => _distributions,
        },
        new KeyStrings());

    private void AddWindowsDefaults()
    {
        _files.Add(Path.Combine(System32, "WindowsPowerShell", "v1.0", "powershell.exe"));
        _files.Add(Path.Combine(System32, "cmd.exe"));
    }

    [Fact]
    public void A_bare_windows_offers_windows_powershell_first_then_the_command_prompt()
    {
        AddWindowsDefaults();

        var shells = Catalog().GetShells();

        Assert.Equal(["powershell", "cmd"], shells.Select(s => s.Id));
        Assert.Equal("Windows PowerShell", shells[0].Name);
        Assert.Equal(["-NoLogo"], shells[0].Arguments);
        Assert.Equal("LocalShell_CommandPrompt", shells[1].Name);
        Assert.Empty(shells[1].Arguments);
    }

    [Fact]
    public void PowerShell_7_comes_first_when_installed()
    {
        AddWindowsDefaults();
        var pwsh = Path.Combine(ProgramFiles, "PowerShell", "7", "pwsh.exe");
        _files.Add(pwsh);

        var shells = Catalog().GetShells();

        Assert.Equal("pwsh", shells[0].Id);
        Assert.Equal(pwsh, shells[0].Path);
    }

    [Fact]
    public void PowerShell_7_from_the_store_is_found_on_the_path()
    {
        AddWindowsDefaults();
        var apps = @"C:\Users\alex\AppData\Local\Microsoft\WindowsApps";
        _variables["PATH"] = string.Join(';', [@"C:\Outils", $"\"{apps}\""]);
        _files.Add(Path.Combine(apps, "pwsh.exe"));

        var shells = Catalog().GetShells();

        Assert.Equal("pwsh", shells[0].Id);
        Assert.Equal(Path.Combine(apps, "pwsh.exe"), shells[0].Path);
    }

    [Fact]
    public void Each_wsl_distribution_gets_its_own_entry_when_wsl_is_present()
    {
        AddWindowsDefaults();
        _distributions.AddRange(["Ubuntu", "Debian"]);
        var withoutWsl = Catalog().GetShells();
        _files.Add(Path.Combine(System32, "wsl.exe"));

        var shells = Catalog().GetShells();

        Assert.DoesNotContain(withoutWsl, s => s.Id.StartsWith("wsl:", StringComparison.Ordinal));
        var ubuntu = Assert.Single(shells, s => s.Id == "wsl:Ubuntu");
        Assert.Equal("Ubuntu", ubuntu.Name);
        Assert.Equal(["-d", "Ubuntu", "--cd", "~"], ubuntu.Arguments);
        Assert.Contains(shells, s => s.Id == "wsl:Debian");
    }

    [Fact]
    public void Git_bash_is_found_in_a_per_user_install()
    {
        AddWindowsDefaults();
        _variables["LOCALAPPDATA"] = @"C:\Users\alex\AppData\Local";
        var bash = Path.Combine(@"C:\Users\alex\AppData\Local", "Programs", "Git", "bin", "bash.exe");
        _files.Add(bash);

        var git = Assert.Single(Catalog().GetShells(), s => s.Id == "gitbash");

        Assert.Equal(bash, git.Path);
        Assert.Equal(["--login", "-i"], git.Arguments);
    }

    [Fact]
    public void Resolve_prefers_the_requested_shell_and_falls_back_to_the_first()
    {
        AddWindowsDefaults();
        var shells = Catalog().GetShells();

        Assert.Equal("cmd", LocalShellCatalog.Resolve(shells, "CMD")?.Id);
        Assert.Equal("powershell", LocalShellCatalog.Resolve(shells, "pwsh")?.Id);
        Assert.Equal("powershell", LocalShellCatalog.Resolve(shells, null)?.Id);
        Assert.Null(LocalShellCatalog.Resolve([], "cmd"));
    }

    [Fact]
    public void Command_line_quotes_arguments_with_spaces()
    {
        var shell = new Quai.Core.Models.LocalShell("wsl:Ubuntu 24.04", "Ubuntu 24.04", @"C:\Windows\System32\wsl.exe", ["-d", "Ubuntu 24.04"]);

        Assert.Equal(@"C:\Windows\System32\wsl.exe -d ""Ubuntu 24.04""", shell.CommandLine);
    }
}
