using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Quai.Core.ViewModels;
using Windows.System;

namespace Quai.App.Views;

/// <summary>Carnet de connexions : filtres à gauche, liste à droite.</summary>
public sealed partial class ConnectionsPage : UserControl
{
    // Ligne visée par le menu contextuel (le DataContext d'un élément de menu n'est pas toujours hérité).
    private ConnectionItemViewModel? _menuTarget;

    public ConnectionsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ConnectionsViewModel>();
        InitializeComponent();
    }

    public ConnectionsViewModel ViewModel { get; }

    private void OnRowMenuOpening(object? sender, object e) =>
        _menuTarget = ((sender as FlyoutBase)?.Target as FrameworkElement)?.DataContext as ConnectionItemViewModel;

    private ConnectionItemViewModel? ItemOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as ConnectionItemViewModel ?? _menuTarget;

    private void OnConnectionDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        ItemOf(sender)?.OpenTerminalCommand.Execute(null);

    private void OnOpenTerminalClick(object sender, RoutedEventArgs e) => ItemOf(sender)?.OpenTerminalCommand.Execute(null);

    private void OnOpenSftpClick(object sender, RoutedEventArgs e) => ItemOf(sender)?.OpenSftpCommand.Execute(null);

    private void OnEditClick(object sender, RoutedEventArgs e) => ItemOf(sender)?.EditCommand.Execute(null);

    private void OnDuplicateClick(object sender, RoutedEventArgs e) => ItemOf(sender)?.DuplicateCommand.Execute(null);

    private void OnDeleteClick(object sender, RoutedEventArgs e) => ItemOf(sender)?.DeleteCommand.Execute(null);

    private void OnFolderToggleClick(object sender, RoutedEventArgs e) =>
        ViewModel.ToggleFolder((sender as FrameworkElement)?.DataContext as ConnectionFilterItem);

    /// <summary>Flèche gauche : replier (ou remonter au parent) ; flèche droite : déplier.</summary>
    private void OnFilterListPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var handled = e.Key switch
        {
            VirtualKey.Left => ViewModel.CollapseSelected(),
            VirtualKey.Right => ViewModel.ExpandSelected(),
            _ => false,
        };
        if (!handled)
        {
            return;
        }

        e.Handled = true;

        // Le focus clavier suit la sélection (l'élément qui l'avait peut avoir été replié).
        if (ViewModel.SelectedFilter is { } selected)
        {
            FilterList.ScrollIntoView(selected);
            (FilterList.ContainerFromItem(selected) as Control)?.Focus(FocusState.Keyboard);
        }
    }
}
