using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Quai.App.Localization;
using Quai.Core.Local;
using Quai.Core.Services;
using Quai.Core.ViewModels;

namespace Quai.App.Views;

/// <summary>Terminaux ouverts (SSH, port série ou shell local), un onglet par session.</summary>
public sealed partial class TerminalsPage : UserControl
{
    private const string TerminalGlyph = "";
    private const string SerialGlyph = "";
    private const string WarningGlyph = "";
    private const string BreakGlyph = "";
    private const string SnippetGlyph = "\uE943";
    private const string LocalGlyph = "\uE977";
    private const string AdminGlyph = "\uEA18";
    private const string FolderGlyph = "\uE8B7";

    private readonly IShellNavigator _navigator;
    private readonly ISessionLauncher _launcher;
    private readonly IDialogService _dialogs;

    public TerminalsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<TerminalsViewModel>();
        _navigator = App.Current.Services.GetRequiredService<IShellNavigator>();
        _launcher = App.Current.Services.GetRequiredService<ISessionLauncher>();
        _dialogs = App.Current.Services.GetRequiredService<IDialogService>();
        InitializeComponent();

        foreach (var tab in ViewModel.Tabs)
        {
            AddTab(tab);
        }

        ViewModel.Tabs.CollectionChanged += OnTabsChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SelectTab(ViewModel.SelectedTab);
    }

    public TerminalsViewModel ViewModel { get; }

    /// <summary>Vue du terminal de l'onglet sélectionné (test de fumée).</summary>
    internal TerminalView? SelectedTerminal => (Tabs.SelectedItem as TabViewItem)?.Content as TerminalView;

    private static FontIconSource Icon(string glyph) => new() { Glyph = glyph };

    private static string GlyphOf(TerminalTabViewModel tab) =>
        tab.CanReconnect ? WarningGlyph : tab.IsSerial ? SerialGlyph : tab.IsLocal ? LocalGlyph : TerminalGlyph;

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in Tabs.TabItems.OfType<TabViewItem>().ToList())
            {
                RemoveItem(item);
            }

            return;
        }

        foreach (var removed in e.OldItems?.OfType<TerminalTabViewModel>() ?? [])
        {
            var item = FindItem(removed);
            if (item is not null)
            {
                RemoveItem(item);
            }
        }

        foreach (var added in e.NewItems?.OfType<TerminalTabViewModel>() ?? [])
        {
            AddTab(added);
        }
    }

    private void AddTab(TerminalTabViewModel tab)
    {
        var view = new TerminalView(tab);
        view.NewLocalTerminalRequested += () => OpenLocalTerminal();
        var item = new TabViewItem
        {
            Header = tab.Title,
            IconSource = Icon(GlyphOf(tab)),
            Content = view,
            Tag = tab,
        };
        ToolTipService.SetToolTip(item, tab.Endpoint);

        // Clic droit sur l'onglet (dans le terminal, le clic droit copie ou colle, comme Windows Terminal).
        var menu = new MenuFlyout();
        var snippets = new MenuFlyoutItem { Text = Loc.Get("Terminal_Snippets"), Icon = new FontIcon { Glyph = SnippetGlyph } };
        snippets.Click += async (_, _) => await RunSnippetAsync(item);
        menu.Items.Add(snippets);
        if (tab.IsSsh)
        {
            var sftp = new MenuFlyoutItem { Text = Loc.Get("Terminal_SftpHere"), Icon = new FontIcon { Glyph = FolderGlyph } };
            sftp.Click += async (_, _) => await OpenSftpHereAsync(tab);
            menu.Items.Add(sftp);
        }
        else if (tab.IsSerial)
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = Loc.Get("Terminal_SendBreak"),
                Command = tab.SendBreakCommand,
                Icon = new FontIcon { Glyph = BreakGlyph },
            });
        }

        item.ContextFlyout = menu;

        tab.PropertyChanged += OnTabPropertyChanged;
        Tabs.TabItems.Add(item);
    }

    private void RemoveItem(TabViewItem item)
    {
        if (item.Tag is TerminalTabViewModel tab)
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
        }

        Tabs.TabItems.Remove(item);
        (item.Content as TerminalView)?.Close();
    }

    private TabViewItem? FindItem(TerminalTabViewModel? tab) =>
        tab is null ? null : Tabs.TabItems.OfType<TabViewItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, tab));

    private void SelectTab(TerminalTabViewModel? tab)
    {
        var item = FindItem(tab);
        if (item is not null && !ReferenceEquals(Tabs.SelectedItem, item))
        {
            Tabs.SelectedItem = item;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalsViewModel.SelectedTab))
        {
            SelectTab(ViewModel.SelectedTab);
        }
    }

    /// <summary>L'icône de l'onglet signale une session fermée ou en échec ; son titre suit la fiche (connexion rapide ajoutée au carnet).</summary>
    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TerminalTabViewModel tab || FindItem(tab) is not { } item)
        {
            return;
        }

        if (e.PropertyName == nameof(TerminalTabViewModel.Status))
        {
            item.IconSource = Icon(GlyphOf(tab));
        }
        else if (e.PropertyName == nameof(TerminalTabViewModel.Title))
        {
            item.Header = tab.Title;
            ToolTipService.SetToolTip(item, tab.Endpoint);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tabs.SelectedItem is TabViewItem { Tag: TerminalTabViewModel tab } item)
        {
            ViewModel.SelectedTab = tab;
            SftpHereButton.IsEnabled = tab.IsSsh;
            (item.Content as TerminalView)?.FocusTerminal();
        }
    }

    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Tag is TerminalTabViewModel tab)
        {
            await ViewModel.CloseAsync(tab);
        }
    }

    private static Task RunSnippetAsync(TabViewItem? item) =>
        (item?.Content as TerminalView)?.RunSnippetAsync() ?? Task.CompletedTask;

    private async void OnSnippetsClick(object sender, RoutedEventArgs e) => await RunSnippetAsync(Tabs.SelectedItem as TabViewItem);

    /// <summary>Explorateur SFTP de la connexion de l'onglet, ouvert dans le dossier courant de son shell.</summary>
    internal async Task OpenSftpHereAsync(TerminalTabViewModel? tab)
    {
        if (tab is null || !tab.IsSsh)
        {
            return;
        }

        try
        {
            await _launcher.OpenSftpHereAsync(tab);
        }
        catch (Exception ex)
        {
            App.Current.Services.GetRequiredService<ILogger<TerminalsPage>>().LogError(ex, "Ouverture de l'explorateur SFTP impossible");
        }
    }

    private async void OnSftpHereClick(object sender, RoutedEventArgs e) => await OpenSftpHereAsync(ViewModel.SelectedTab);

    /// <summary>Terminal local : shell <paramref name="shellId"/>, sinon celui des réglages (premier shell détecté par défaut).</summary>
    internal void OpenLocalTerminal(string? shellId = null) => _launcher.OpenLocalTerminal(shellId);

    private void OnLocalTerminalClick(SplitButton sender, SplitButtonClickEventArgs args) => OpenLocalTerminal();

    private void OnEmptyLocalTerminalClick(object sender, RoutedEventArgs e) => OpenLocalTerminal();

    private void OnLocalTerminalAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenLocalTerminal();
    }

    /// <summary>Menu de la flèche : les shells installés, relus à chaque ouverture (un shell a pu être installé entre-temps).</summary>
    private void OnShellMenuOpening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu)
        {
            return;
        }

        menu.Items.Clear();
        var shells = ViewModel.GetLocalShells();
        if (shells.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = Loc.Get("Terminals_NoLocalShell"), IsEnabled = false });
            return;
        }

        foreach (var shell in shells)
        {
            var item = new MenuFlyoutItem { Text = shell.Name, Icon = new FontIcon { Glyph = LocalGlyph } };
            ToolTipService.SetToolTip(item, shell.CommandLine);
            var id = shell.Id;
            item.Click += (_, _) => OpenLocalTerminal(id);
            menu.Items.Add(item);
        }

        // En administrateur : fenêtre Windows séparée (voir ElevatedShellLauncher), jamais un onglet de Quai.
        var admins = ViewModel.GetAdminShells();
        if (admins.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        foreach (var shell in admins)
        {
            var item = new MenuFlyoutItem
            {
                Text = Loc.Format("Terminals_AsAdmin", shell.Name),
                Icon = new FontIcon { Glyph = AdminGlyph },
            };
            ToolTipService.SetToolTip(item, Loc.Format("Terminals_AdminTooltip", shell.Name));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(item, Loc.Format("Terminals_AdminTooltip", shell.Name));
            var id = shell.Id;
            item.Click += async (_, _) => await OpenAdminTerminalAsync(id);
            menu.Items.Add(item);
        }
    }

    /// <summary>Shell administrateur : Windows demande confirmation puis l'ouvre dans sa propre fenêtre.</summary>
    internal async Task OpenAdminTerminalAsync(string shellId)
    {
        if (ViewModel.OpenAdmin(shellId, out var error) == ElevatedLaunchResult.Failed)
        {
            await _dialogs.ShowErrorAsync(Loc.Get("Terminal_AdminFailedTitle"), error);
        }
    }

    private void OnAddTabClick(TabView sender, object args) => _navigator.NavigateTo(AppPage.Connections);

    private void OnOpenConnectionsClick(object sender, RoutedEventArgs e) => _navigator.NavigateTo(AppPage.Connections);
}
