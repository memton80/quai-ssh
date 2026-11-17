using Microsoft.Extensions.Time.Testing;
using Quai.Core.Models;
using Quai.Core.Ssh;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.ViewModels;

public class AutoReconnectTests
{
    private readonly FakeSessionFactory _factory = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ConnectionProfile _profile = new() { Name = "Serveur", Host = "example.org", Username = "alice" };

    private TerminalTabViewModel CreateTab(ConnectionProfile? profile = null) =>
        new(profile ?? _profile, _factory, new KeyStrings(), new ImmediateDispatcher(), settings: _settings, time: _time);

    private async Task<(TerminalTabViewModel Tab, FakeTerminalSession Session)> ConnectedTabAsync(ConnectionProfile? profile = null)
    {
        var session = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => session);
        var tab = CreateTab(profile);
        await tab.ConnectAsync(100, 30);
        Assert.True(tab.IsConnected);
        return (tab, session);
    }

    /// <summary>
    /// Avance l'horloge seconde par seconde ; avant chaque pas, attend que la boucle de reconnexion soit de nouveau
    /// en attente sur l'horloge (ses continuations peuvent passer par un autre fil : pas de délai fixe).
    /// </summary>
    private async Task AdvanceAsync(int seconds)
    {
        for (var i = 0; i < seconds; i++)
        {
            await SettleAsync();
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        await SettleAsync();
    }

    /// <summary>Laisse s'exécuter ce qui est prêt : jusqu'à ce que plus rien ne change pendant quelques millisecondes.</summary>
    private static async Task SettleAsync()
    {
        for (var i = 0; i < 4; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Attend (5 s au plus) qu'une condition devienne vraie.</summary>
    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_lost_connection_is_retried_after_a_countdown()
    {
        var (tab, session) = await ConnectedTabAsync();
        var next = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => next);

        session.Close(new IOException("réseau"));

        Assert.True(tab.IsAutoReconnecting);
        Assert.True(tab.ShowReconnectBar);
        Assert.Equal("Terminal_ReconnectIn(Terminal_ConnectionLost(réseau),2,1,6)", tab.StatusText);
        await AdvanceAsync(1);
        Assert.Equal("Terminal_ReconnectIn(Terminal_ConnectionLost(réseau),1,1,6)", tab.StatusText);
        Assert.Single(_factory.TerminalRequests);

        await AdvanceAsync(1);
        await UntilAsync(() => tab.IsConnected);

        Assert.True(tab.IsConnected);
        Assert.False(tab.IsAutoReconnecting);
        Assert.False(tab.ShowReconnectBar);
        Assert.True(next.Started);
        Assert.Equal((100, 30), (_factory.TerminalRequests[1].Columns, _factory.TerminalRequests[1].Rows));
    }

    [Fact]
    public async Task A_normal_exit_is_not_retried()
    {
        var (tab, session) = await ConnectedTabAsync();

        session.Close();
        await AdvanceAsync(10);

        Assert.False(tab.IsAutoReconnecting);
        Assert.Equal("Terminal_Closed", tab.StatusText);
        Assert.Single(_factory.TerminalRequests);
    }

    [Fact]
    public async Task Nothing_is_retried_when_the_setting_is_off()
    {
        _settings.Update(s => s.AutoReconnect = false);
        var (tab, session) = await ConnectedTabAsync();

        session.Close(new IOException("réseau"));
        await AdvanceAsync(10);

        Assert.False(tab.IsAutoReconnecting);
        Assert.True(tab.CanReconnect);
        Assert.Single(_factory.TerminalRequests);
    }

    [Fact]
    public async Task A_serial_link_is_not_retried()
    {
        var serial = new ConnectionProfile { Protocol = ConnectionProtocol.Serial, Serial = new SerialSettings { PortName = "COM3" } };
        var (tab, session) = await ConnectedTabAsync(serial);

        session.Close(new IOException("débranché"));

        Assert.False(tab.IsAutoReconnecting);
    }

    [Fact]
    public async Task Network_failures_keep_trying_but_a_refused_login_stops()
    {
        var (tab, session) = await ConnectedTabAsync();
        _factory.Terminals.Enqueue(() => throw new IOException("injoignable"));
        _factory.Terminals.Enqueue(() => throw new SshLoginFailedException("refusé"));

        session.Close(new IOException("réseau"));
        await AdvanceAsync(2);

        Assert.True(tab.IsAutoReconnecting);
        Assert.Equal("Terminal_ReconnectIn(Terminal_ConnectFailed(injoignable),5,2,6)", tab.StatusText);

        await AdvanceAsync(5);
        await UntilAsync(() => !tab.IsAutoReconnecting);

        Assert.False(tab.IsAutoReconnecting);
        Assert.Equal(TerminalStatus.Failed, tab.Status);
        Assert.Equal("Terminal_AuthenticationFailed", tab.StatusText);
        Assert.Equal(3, _factory.TerminalRequests.Count);
    }

    [Fact]
    public async Task It_gives_up_after_the_last_attempt()
    {
        var (tab, session) = await ConnectedTabAsync();
        for (var i = 0; i < TerminalTabViewModel.ReconnectDelays.Count; i++)
        {
            _factory.Terminals.Enqueue(() => throw new IOException("injoignable"));
        }

        session.Close(new IOException("réseau"));
        await AdvanceAsync((int)TerminalTabViewModel.ReconnectDelays.Sum(d => d.TotalSeconds));
        await UntilAsync(() => !tab.IsAutoReconnecting);

        Assert.False(tab.IsAutoReconnecting);
        Assert.Equal("Terminal_ReconnectGaveUp(Terminal_ConnectFailed(injoignable),6)", tab.StatusText);
        Assert.Equal(7, _factory.TerminalRequests.Count);
        Assert.True(tab.CanReconnect);
    }

    [Fact]
    public async Task Stopping_cancels_the_pending_attempts()
    {
        var (tab, session) = await ConnectedTabAsync();
        session.Close(new IOException("réseau"));

        Assert.True(tab.CancelAutoReconnectCommand.CanExecute(null));
        tab.CancelAutoReconnectCommand.Execute(null);
        await AdvanceAsync(10);

        Assert.False(tab.IsAutoReconnecting);
        Assert.False(tab.CancelAutoReconnectCommand.CanExecute(null));
        Assert.Equal("Terminal_ReconnectStopped(Terminal_ConnectionLost(réseau))", tab.StatusText);
        Assert.Single(_factory.TerminalRequests);
    }

    [Fact]
    public async Task Reconnecting_by_hand_skips_the_countdown()
    {
        var (tab, session) = await ConnectedTabAsync();
        _factory.Terminals.Enqueue(() => new FakeTerminalSession());
        session.Close(new IOException("réseau"));

        await tab.ReconnectCommand.ExecuteAsync(null);
        await AdvanceAsync(10);

        Assert.True(tab.IsConnected);
        Assert.False(tab.IsAutoReconnecting);
        Assert.Equal(2, _factory.TerminalRequests.Count);
    }

    [Fact]
    public async Task Closing_the_tab_cancels_the_pending_attempts()
    {
        var (tab, session) = await ConnectedTabAsync();
        session.Close(new IOException("réseau"));

        await tab.DisposeAsync();
        await AdvanceAsync(10);

        Assert.Single(_factory.TerminalRequests);
    }

    [Fact]
    public async Task A_new_loss_after_a_successful_retry_starts_over()
    {
        var (tab, session) = await ConnectedTabAsync();
        var second = new FakeTerminalSession();
        _factory.Terminals.Enqueue(() => second);
        session.Close(new IOException("réseau"));
        await AdvanceAsync(2);
        await UntilAsync(() => tab.IsConnected);
        Assert.True(tab.IsConnected);

        second.Close(new IOException("encore"));

        Assert.Equal("Terminal_ReconnectIn(Terminal_ConnectionLost(encore),2,1,6)", tab.StatusText);
    }
}
