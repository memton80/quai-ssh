using Quai.Core.Models;
using Quai.Core.Storage;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.Storage;

public class SettingsServiceTests
{
    [Fact]
    public void Defaults_apply_when_there_is_no_file()
    {
        using var temp = new TempDirectory();
        var settings = new JsonSettingsService(temp.File("settings.json")).Current;

        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal("Cascadia Mono", settings.TerminalFontFamily);
        Assert.Equal(12, settings.TerminalFontSize);
    }

    [Fact]
    public void Updates_are_persisted_and_announced_once()
    {
        using var temp = new TempDirectory();
        var path = temp.File("settings.json");
        var service = new JsonSettingsService(path);
        var notifications = 0;
        service.Changed += (_, _) => notifications++;

        service.Update(s =>
        {
            s.Theme = AppTheme.Dark;
            s.TerminalFontFamily = " Consolas ";
            s.TerminalFontSize = 18;
        });
        service.Update(s => s.Theme = AppTheme.Dark);

        Assert.Equal(1, notifications);
        var reloaded = new JsonSettingsService(path).Current;
        Assert.Equal(AppTheme.Dark, reloaded.Theme);
        Assert.Equal("Consolas", reloaded.TerminalFontFamily);
        Assert.Equal(18, reloaded.TerminalFontSize);
        Assert.Contains("\"theme\": \"dark\"", File.ReadAllText(path));
    }

    [Fact]
    public void Current_is_a_copy()
    {
        using var temp = new TempDirectory();
        var service = new JsonSettingsService(temp.File("settings.json"));

        service.Current.TerminalFontSize = 30;

        Assert.Equal(12, service.Current.TerminalFontSize);
    }

    [Theory]
    [InlineData(2, 8)]
    [InlineData(500, 32)]
    [InlineData(16, 16)]
    public void Font_size_is_clamped(int requested, int expected)
    {
        using var temp = new TempDirectory();
        var service = new JsonSettingsService(temp.File("settings.json"));

        service.Update(s => s.TerminalFontSize = requested);

        Assert.Equal(expected, service.Current.TerminalFontSize);
    }

    [Fact]
    public void Out_of_range_values_in_the_file_are_repaired()
    {
        using var temp = new TempDirectory();
        var path = temp.File("settings.json");
        File.WriteAllText(path, """{ "theme": 42, "terminalFontFamily": "  ", "terminalFontSize": -3 }""");

        var settings = new JsonSettingsService(path).Current;

        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal("Cascadia Mono", settings.TerminalFontFamily);
        Assert.Equal(8, settings.TerminalFontSize);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_defaults_and_is_set_aside()
    {
        using var temp = new TempDirectory();
        var path = temp.File("settings.json");
        File.WriteAllText(path, "pas du json");

        var service = new JsonSettingsService(path);

        Assert.Equal(12, service.Current.TerminalFontSize);
        Assert.Single(Directory.GetFiles(temp.Path, "settings.json.corrompu-*"));
    }

    [Fact]
    public void The_view_model_reflects_and_saves_each_change()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        var service = new JsonSettingsService(paths);
        service.Update(s => s.Theme = AppTheme.Light);
        var notifications = 0;
        service.Changed += (_, _) => notifications++;

        var vm = new SettingsViewModel(service, paths);
        Assert.Equal(1, vm.ThemeIndex);
        Assert.Equal(12, vm.TerminalFontSize);
        Assert.Equal(0, notifications);

        vm.ThemeIndex = 2;
        vm.TerminalFontFamily = "Consolas";
        vm.TerminalFontSize = 17.6;

        var saved = new JsonSettingsService(paths).Current;
        Assert.Equal(AppTheme.Dark, saved.Theme);
        Assert.Equal("Consolas", saved.TerminalFontFamily);
        Assert.Equal(18, saved.TerminalFontSize);
        Assert.Equal(3, notifications);
        Assert.Equal(temp.Path, vm.DataDirectory);
        Assert.Contains("Consolas", vm.SuggestedFonts);
    }

    [Fact]
    public void Transient_empty_input_is_not_saved_and_reset_restores_defaults()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        var service = new JsonSettingsService(paths);
        var vm = new SettingsViewModel(service, paths) { TerminalFontFamily = "Consolas", TerminalFontSize = 20 };

        vm.TerminalFontFamily = string.Empty;
        vm.TerminalFontSize = double.NaN;
        vm.ThemeIndex = 9;
        Assert.Equal("Consolas", service.Current.TerminalFontFamily);
        Assert.Equal(20, service.Current.TerminalFontSize);
        Assert.Equal(AppTheme.System, service.Current.Theme);

        vm.ResetTerminalFontCommand.Execute(null);
        Assert.Equal("Cascadia Mono", service.Current.TerminalFontFamily);
        Assert.Equal(12, service.Current.TerminalFontSize);
        Assert.Equal(12, vm.TerminalFontSize);
    }

    [Fact]
    public void The_version_has_no_build_metadata()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", AppInfo.Version);
        Assert.DoesNotContain("+", AppInfo.Version);
    }
}
