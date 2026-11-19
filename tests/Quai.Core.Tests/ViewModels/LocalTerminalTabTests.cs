using System.Text;
using Quai.Core.Local;
using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.ViewModels;

public sealed class LocalTerminalTabTests
{
    private static readonly LocalShell PowerShell = new("pwsh", "PowerShell", @"C:\Program Files\PowerShell\7\pwsh.exe", ["-NoLogo"]);
    private static readonly LocalShell Cmd = new("cmd", "Invite de commandes", @"C:\Windows\System32\cmd.exe", []);

    private readonly FakeSessionFactory _factory = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly FakeShells _shells = new([PowerShell, Cmd]);

    private TerminalsViewModel Terminals() =>
        new(_factory, new KeyStrings(), new ImmediateDispatcher(), settings: _settings, shells: _shells);

    [Fact]
    public async Task The_local_button_opens_the_shell_chosen_in_the_settings()
    {
        _settings.Update(s => s.LocalShell = "cmd");
        _factory.Terminals.Enqueue(() => new FakeTerminalSession());
        var terminals = Terminals();

        var tab = terminals.OpenLocal();
        await tab!.ConnectAsync(100, 30);

        Assert.Same(tab, terminals.SelectedTab);
        Assert.Equal("Invite de commandes", tab.Title);
        Assert.Equal(Cmd.Path, tab.Endpoint);
        Assert.True(tab.IsLocal);
        Assert.False(tab.IsSsh);
        var request = Assert.Single(_factory.TerminalRequests);
        Assert.Equal(ConnectionProtocol.Local, request.Profile.Protocol);
        Assert.Equal(Cmd, request.Profile.LocalShell);
        Assert.Equal((100, 30), (request.Columns, request.Rows));
    }

    [Fact]
    public void An_explicit_choice_wins_and_an_uninstalled_shell_falls_back_to_the_first()
    {
        _settings.Update(s => s.LocalShell = "wsl:Ubuntu");
        var terminals = Terminals();

        Assert.Equal("PowerShell", terminals.OpenLocal()!.Title);
        Assert.Equal("Invite de commandes", terminals.OpenLocal("cmd")!.Title);
    }

    [Fact]
    public void Without_any_shell_nothing_is_opened()
    {
        var terminals = new TerminalsViewModel(_factory, new KeyStrings(), new ImmediateDispatcher(), shells: new FakeShells([]));

        Assert.Null(terminals.OpenLocal());
        Assert.True(terminals.IsEmpty);
    }

    [Fact]
    public async Task A_local_shell_starts_silently_and_is_never_offered_to_the_connection_book()
    {
        _factory.Terminals.Enqueue(() => new FakeTerminalSession());
        var tab = Terminals().OpenLocal()!;
        var output = new StringBuilder();
        tab.Output += data => output.Append(Encoding.UTF8.GetString(data));

        await tab.ConnectAsync(80, 24);

        Assert.True(tab.IsConnected);
        Assert.Equal(string.Empty, output.ToString());
        Assert.False(tab.IsQuickConnect);
        Assert.False(tab.ShowSaveOffer);
        Assert.Equal("Terminal_Restart", tab.ReconnectText);
        Assert.Null(await tab.GetWorkingDirectoryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Exit_shows_the_shell_name_and_restart_opens_a_new_shell_without_automatic_retries()
    {
        var first = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => first);
        _factory.Terminals.Enqueue(() => new FakeTerminalSession());
        var tab = Terminals().OpenLocal()!;
        var output = new StringBuilder();
        tab.Output += data => output.Append(Encoding.UTF8.GetString(data));
        await tab.ConnectAsync(80, 24);

        first.Close(new LocalShellExitedException(1));

        Assert.Equal(TerminalStatus.Disconnected, tab.Status);
        Assert.Equal("Terminal_LocalExitedWithCode(PowerShell,1)", tab.StatusText);
        Assert.False(tab.IsAutoReconnecting);

        await tab.ReconnectCommand.ExecuteAsync(null);

        Assert.True(tab.IsConnected);
        Assert.Contains("Terminal_LocalStarting(PowerShell)", output.ToString());
        Assert.Equal(2, _factory.TerminalRequests.Count);
    }

    [Fact]
    public async Task A_normal_exit_reads_as_the_shell_having_ended()
    {
        var session = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => session);
        var tab = Terminals().OpenLocal()!;
        await tab.ConnectAsync(80, 24);

        session.Close();

        Assert.Equal("Terminal_LocalExited(PowerShell)", tab.StatusText);
        Assert.True(tab.CanReconnect);
    }

    [Fact]
    public async Task A_shell_that_cannot_start_explains_why()
    {
        _factory.Terminals.Enqueue(() => throw new LocalShellUnavailableException("PowerShell", new IOException("Accès refusé")));
        var tab = Terminals().OpenLocal()!;

        await tab.ConnectAsync(80, 24);

        Assert.Equal(TerminalStatus.Failed, tab.Status);
        Assert.Equal("Terminal_LocalUnavailable(PowerShell,Accès refusé)", tab.StatusText);
    }

    [Fact]
    public async Task The_launcher_opens_the_terminals_page_and_never_an_sftp_explorer_for_a_local_tab()
    {
        var terminals = Terminals();
        var sftp = new SftpSessionsViewModel(_factory, new FakeDialogService(), new KeyStrings(), new ImmediateDispatcher());
        var navigator = new RecordingNavigator();
        var launcher = new SessionLauncher(terminals, sftp, navigator);

        launcher.OpenLocalTerminal("cmd");
        var tab = terminals.SelectedTab!;
        launcher.OpenSftp(tab.Profile);
        await launcher.OpenSftpHereAsync(tab);

        Assert.Equal("Invite de commandes", tab.Title);
        Assert.Equal([AppPage.Terminals], navigator.Pages);
        Assert.True(sftp.IsEmpty);
    }

    [Fact]
    public void A_local_profile_keeps_no_ssh_or_serial_field()
    {
        var profile = ConnectionProfile.ForLocalShell(Cmd);
        profile.Host = "example.org";
        profile.Serial = new SerialSettings();

        profile.Normalize();

        Assert.Equal(string.Empty, profile.Host);
        Assert.Null(profile.Serial);
        Assert.Equal(Cmd, profile.LocalShell);
        Assert.Equal("Invite de commandes", profile.DisplayName);
        Assert.True(profile.Clone().IsTransient);
    }

    [Fact]
    public void The_settings_page_lists_the_shells_and_saves_the_chosen_one()
    {
        using var temp = new TempDirectory();
        var page = new SettingsViewModel(_settings, new Quai.Core.Storage.AppPaths(temp.Path), _shells);

        Assert.Equal(["PowerShell", "Invite de commandes"], page.LocalShellNames);
        Assert.Equal(0, page.LocalShellIndex);

        page.LocalShellIndex = 1;

        Assert.Equal("cmd", _settings.Current.LocalShell);
        Assert.Equal(1, new SettingsViewModel(_settings, new Quai.Core.Storage.AppPaths(temp.Path), _shells).LocalShellIndex);
    }

    private sealed class FakeShells(IReadOnlyList<LocalShell> shells) : ILocalShellCatalog
    {
        public IReadOnlyList<LocalShell> GetShells() => shells;
    }
}
