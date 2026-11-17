using Quai.Core.Models;
using Quai.Core.ViewModels;

namespace Quai.Core.Services;

/// <summary>Ouvre un onglet pour la connexion choisie et affiche la page correspondante.</summary>
public sealed class SessionLauncher : ISessionLauncher
{
    private readonly TerminalsViewModel _terminals;
    private readonly SftpSessionsViewModel _sftp;
    private readonly IShellNavigator _navigator;

    public SessionLauncher(TerminalsViewModel terminals, SftpSessionsViewModel sftp, IShellNavigator navigator)
    {
        _terminals = terminals;
        _sftp = sftp;
        _navigator = navigator;
    }

    public void OpenTerminal(ConnectionProfile profile)
    {
        _terminals.Open(profile);
        _navigator.NavigateTo(AppPage.Terminals);
    }

    public void OpenSftp(ConnectionProfile profile)
    {
        if (profile.IsSerial)
        {
            return;
        }

        _sftp.Open(profile);
        _navigator.NavigateTo(AppPage.Sftp);
    }

    public async Task OpenSftpHereAsync(TerminalTabViewModel tab)
    {
        if (tab.IsSerial)
        {
            return;
        }

        var directory = await tab.GetWorkingDirectoryAsync();
        await _sftp.OpenAtAsync(tab.Profile, directory);
        _navigator.NavigateTo(AppPage.Sftp);
    }
}
