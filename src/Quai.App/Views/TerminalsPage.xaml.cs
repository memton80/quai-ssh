using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quai.App.Localization;
using Quai.Core.Services;
using Quai.Core.ViewModels;

namespace Quai.App.Views;

/// <summary>Terminaux ouverts (SSH ou port série), un onglet par session.</summary>
public sealed partial class TerminalsPage : UserControl
{
    private const string TerminalGlyph = "";
    private const string SerialGlyph = "";
    private const string WarningGlyph = "";
    private const string BreakGlyph = "";
    private const string SnippetGlyph = "\uE943";

    private readonly IShellNavigator _navigator;

    public TerminalsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<TerminalsViewModel>();
        _navigator = App.Current.Services.GetRequiredService<IShellNavigator>();
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
        tab.CanReconnect ? WarningGlyph : tab.IsSerial ? SerialGlyph : TerminalGlyph;

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
        var item = new TabViewItem
        {
            Header = tab.Title,
            IconSource = Icon(GlyphOf(tab)),
            Content = new TerminalView(tab),
            Tag = tab,
        };
        ToolTipService.SetToolTip(item, tab.Endpoint);

        // Clic droit sur l'onglet (dans le terminal, le clic droit copie ou colle, comme Windows Terminal).
        var menu = new MenuFlyout();
        var snippets = new MenuFlyoutItem { Text = Loc.Get("Terminal_Snippets"), Icon = new FontIcon { Glyph = SnippetGlyph } };
        snippets.Click += async (_, _) => await RunSnippetAsync(item);
        menu.Items.Add(snippets);
        if (tab.IsSerial)
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

    private void OnAddTabClick(TabView sender, object args) => _navigator.NavigateTo(AppPage.Connections);

    private void OnOpenConnectionsClick(object sender, RoutedEventArgs e) => _navigator.NavigateTo(AppPage.Connections);
}
