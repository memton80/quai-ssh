using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Quai.Core.Localization;
using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Ssh;

namespace Quai.Core.ViewModels;

/// <summary>Les explorateurs SFTP ouverts (un onglet par session).</summary>
public sealed partial class SftpSessionsViewModel : ObservableObject
{
    private readonly ISshSessionFactory _factory;
    private readonly IDialogService _dialogs;
    private readonly IStrings _strings;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILocalRecycler? _recycler;
    private readonly ILoggerFactory? _loggerFactory;

    public SftpSessionsViewModel(
        ISshSessionFactory factory,
        IDialogService dialogs,
        IStrings strings,
        IUiDispatcher dispatcher,
        ILocalRecycler? recycler = null,
        ILoggerFactory? loggerFactory = null)
    {
        _factory = factory;
        _dialogs = dialogs;
        _strings = strings;
        _dispatcher = dispatcher;
        _recycler = recycler;
        _loggerFactory = loggerFactory;
        IsEmpty = true;
    }

    public ObservableCollection<SftpBrowserViewModel> Tabs { get; } = [];

    [ObservableProperty]
    public partial SftpBrowserViewModel? SelectedTab { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>Ajoute un onglet et lance la connexion, dans <paramref name="remotePath"/> s'il est donné.</summary>
    public SftpBrowserViewModel Open(ConnectionProfile profile, string? remotePath = null)
    {
        var tab = new SftpBrowserViewModel(
            profile.Clone(),
            _factory,
            _dialogs,
            _strings,
            _dispatcher,
            _recycler,
            _loggerFactory?.CreateLogger<SftpBrowserViewModel>(),
            initialRemotePath: remotePath);
        Tabs.Add(tab);
        SelectedTab = tab;
        IsEmpty = false;
        _ = tab.ConnectAsync();
        return tab;
    }

    /// <summary>
    /// Ouverture depuis un terminal : un explorateur déjà connecté à la même connexion est réutilisé (il passe au
    /// dossier demandé), sinon un nouvel onglet s'ouvre dans ce dossier.
    /// </summary>
    public async Task<SftpBrowserViewModel> OpenAtAsync(ConnectionProfile profile, string? remotePath)
    {
        var existing = Tabs.FirstOrDefault(t => t.Profile.Id == profile.Id && t.IsConnected);
        if (existing is null)
        {
            return Open(profile, remotePath);
        }

        SelectedTab = existing;
        if (!string.IsNullOrEmpty(remotePath) && remotePath != existing.RemotePath)
        {
            await existing.NavigateRemoteAsync(remotePath);
        }

        return existing;
    }

    public async Task CloseAsync(SftpBrowserViewModel? tab)
    {
        if (tab is null || !Tabs.Remove(tab))
        {
            return;
        }

        IsEmpty = Tabs.Count == 0;
        if (ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = Tabs.LastOrDefault();
        }

        await tab.DisposeAsync();
    }

    /// <summary>Ferme tous les onglets (fermeture de l'application) : transferts interrompus, sessions déconnectées.</summary>
    public Task CloseAllAsync() => Task.WhenAll(Tabs.ToList().Select(CloseAsync));
}
