using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Quai.Core.Local;
using Quai.Core.Localization;
using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Storage;
using Quai.Core.Terminal;

namespace Quai.Core.ViewModels;

/// <summary>Les terminaux ouverts (un onglet par session SSH, série ou locale).</summary>
public sealed partial class TerminalsViewModel : ObservableObject
{
    private readonly ITerminalSessionFactory _factory;
    private readonly IStrings _strings;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ISettingsService? _settings;
    private readonly ILocalShellCatalog? _shells;

    public TerminalsViewModel(
        ITerminalSessionFactory factory,
        IStrings strings,
        IUiDispatcher dispatcher,
        ILoggerFactory? loggerFactory = null,
        ISettingsService? settings = null,
        ILocalShellCatalog? shells = null)
    {
        _factory = factory;
        _strings = strings;
        _dispatcher = dispatcher;
        _loggerFactory = loggerFactory;
        _settings = settings;
        _shells = shells;
        IsEmpty = true;
    }

    /// <summary>Shells installés, proposés pour un terminal local (relus à chaque appel : un shell a pu être installé).</summary>
    public IReadOnlyList<LocalShell> GetLocalShells() => _shells?.GetShells() ?? [];

    /// <summary>
    /// Ouvre un terminal local avec le shell <paramref name="shellId"/>, sinon celui des réglages, sinon le premier
    /// détecté. <c>null</c> si aucun shell n'est disponible.
    /// </summary>
    public TerminalTabViewModel? OpenLocal(string? shellId = null)
    {
        var shell = LocalShellCatalog.Resolve(GetLocalShells(), shellId ?? _settings?.Current.LocalShell);
        return shell is null ? null : Open(ConnectionProfile.ForLocalShell(shell));
    }

    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    public partial TerminalTabViewModel? SelectedTab { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>Ajoute un onglet ; la connexion démarre quand la vue du terminal est prête.</summary>
    public TerminalTabViewModel Open(ConnectionProfile profile)
    {
        var tab = new TerminalTabViewModel(
            profile.Clone(),
            _factory,
            _strings,
            _dispatcher,
            _loggerFactory?.CreateLogger<TerminalTabViewModel>(),
            _settings);
        Tabs.Add(tab);
        SelectedTab = tab;
        IsEmpty = false;
        return tab;
    }

    public async Task CloseAsync(TerminalTabViewModel? tab)
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

    /// <summary>Ferme tous les onglets (fermeture de l'application) : chaque session est déconnectée, chaque port libéré.</summary>
    public Task CloseAllAsync() => Task.WhenAll(Tabs.ToList().Select(CloseAsync));
}
