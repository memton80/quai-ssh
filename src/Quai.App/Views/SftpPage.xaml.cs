using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quai.Core.Services;
using Quai.Core.ViewModels;

namespace Quai.App.Views;

/// <summary>Explorateurs SFTP ouverts, un onglet par session.</summary>
public sealed partial class SftpPage : UserControl
{
    private const string FolderGlyph = "";
    private const string WarningGlyph = "";

    private readonly IShellNavigator _navigator;

    public SftpPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<SftpSessionsViewModel>();
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

    public SftpSessionsViewModel ViewModel { get; }

    private static FontIconSource Icon(string glyph) => new() { Glyph = glyph };

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var removed in e.OldItems?.OfType<SftpBrowserViewModel>() ?? [])
        {
            if (FindItem(removed) is { } item)
            {
                removed.PropertyChanged -= OnTabPropertyChanged;
                Tabs.TabItems.Remove(item);
            }
        }

        foreach (var added in e.NewItems?.OfType<SftpBrowserViewModel>() ?? [])
        {
            AddTab(added);
        }
    }

    private void AddTab(SftpBrowserViewModel tab)
    {
        var item = new TabViewItem
        {
            Header = tab.Title,
            IconSource = Icon(FolderGlyph),
            Content = new SftpBrowserView(tab),
            Tag = tab,
        };
        ToolTipService.SetToolTip(item, tab.Endpoint);
        tab.PropertyChanged += OnTabPropertyChanged;
        Tabs.TabItems.Add(item);
    }

    private TabViewItem? FindItem(SftpBrowserViewModel? tab) =>
        tab is null ? null : Tabs.TabItems.OfType<TabViewItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, tab));

    private void SelectTab(SftpBrowserViewModel? tab)
    {
        var item = FindItem(tab);
        if (item is not null && !ReferenceEquals(Tabs.SelectedItem, item))
        {
            Tabs.SelectedItem = item;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SftpSessionsViewModel.SelectedTab))
        {
            SelectTab(ViewModel.SelectedTab);
        }
    }

    /// <summary>L'icône de l'onglet signale une session fermée ou en échec.</summary>
    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SftpBrowserViewModel.Status) && sender is SftpBrowserViewModel tab && FindItem(tab) is { } item)
        {
            item.IconSource = Icon(tab.CanReconnect ? WarningGlyph : FolderGlyph);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tabs.SelectedItem is TabViewItem { Tag: SftpBrowserViewModel tab })
        {
            ViewModel.SelectedTab = tab;
        }
    }

    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Tag is SftpBrowserViewModel tab)
        {
            await ViewModel.RequestCloseAsync(tab);
        }
    }

    private void OnAddTabClick(TabView sender, object args) => _navigator.NavigateTo(AppPage.Connections);

    private void OnOpenConnectionsClick(object sender, RoutedEventArgs e) => _navigator.NavigateTo(AppPage.Connections);
}
