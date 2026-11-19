using System.ComponentModel;
using System.Diagnostics;
using Quai.Core.Local;
using Quai.Core.Models;

namespace Quai.Core.Tests.Local;

public sealed class ElevatedShellLauncherTests
{
    private static readonly LocalShell Cmd = new("admin:cmd", "Invite de commandes", @"C:\Windows\System32\cmd.exe", ["/D"]);

    [Fact]
    public void Windows_is_asked_to_start_the_shell_itself_as_administrator()
    {
        ProcessStartInfo? started = null;
        var launcher = new ElevatedShellLauncher(info =>
        {
            started = info;
            return null;
        });

        Assert.Equal(ElevatedLaunchResult.Started, launcher.Launch(Cmd, out var error));

        Assert.Empty(error);
        Assert.NotNull(started);
        Assert.Equal(Cmd.Path, started.FileName);
        Assert.Equal("runas", started.Verb);
        Assert.True(started.UseShellExecute);
        Assert.Equal(["/D"], started.ArgumentList);
    }

    [Fact]
    public void Saying_no_to_the_windows_prompt_is_not_an_error()
    {
        var launcher = new ElevatedShellLauncher(_ => throw new Win32Exception(1223));

        Assert.Equal(ElevatedLaunchResult.Cancelled, launcher.Launch(Cmd, out var error));
        Assert.Empty(error);
    }

    [Fact]
    public void Other_failures_are_reported()
    {
        var launcher = new ElevatedShellLauncher(_ => throw new Win32Exception(2, "Fichier introuvable"));

        Assert.Equal(ElevatedLaunchResult.Failed, launcher.Launch(Cmd, out var error));
        Assert.Equal("Fichier introuvable", error);
    }

    [Fact]
    public void Only_shells_from_the_administrator_list_can_be_elevated()
    {
        var launcher = new ElevatedShellLauncher(_ => throw new InvalidOperationException("ne doit pas être lancé"));

        Assert.Throws<ArgumentException>(() => launcher.Launch(Cmd with { Id = "cmd" }, out _));
        Assert.Throws<ArgumentException>(() => launcher.Launch(Cmd with { Path = "cmd.exe" }, out _));
    }
}
