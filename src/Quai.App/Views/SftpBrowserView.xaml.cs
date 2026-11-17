using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Quai.App.Localization;
using Quai.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Quai.App.Views;

/// <summary>
/// Explorateur SFTP à double panneau : dossier local à gauche, serveur à droite, transferts en bas.
/// Glisser-déposer entre les panneaux, et depuis l'Explorateur de fichiers vers le serveur.
/// </summary>
public sealed partial class SftpBrowserView : UserControl
{
    private enum DragSource
    {
        None,
        Local,
        Remote,
    }

    // Éléments en cours de glissement à l'intérieur de la vue (les éléments distants n'ont pas de fichier réel).
    private DragSource _dragSource;
    private List<FileItemViewModel> _dragItems = [];

    public SftpBrowserView(SftpBrowserViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public SftpBrowserViewModel ViewModel { get; }

    private static List<FileItemViewModel> Selection(ListView list) =>
        [.. list.SelectedItems.OfType<FileItemViewModel>()];

    private static FileItemViewModel? ItemOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as FileItemViewModel;

    /// <summary>Clic droit sur une ligne non sélectionnée : elle devient la sélection (cible du menu).</summary>
    private static void SelectForContextMenu(ListView list, object sender)
    {
        if (ItemOf(sender) is { } item && !list.SelectedItems.Contains(item))
        {
            list.SelectedItem = item;
        }
    }

    // ---- Panneau local -----------------------------------------------------------------------

    private void OnLocalPathKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            ViewModel.NavigateLocal(LocalPathBox.Text);
        }
    }

    private void OnLocalItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ViewModel.OpenLocal(ItemOf(sender));

    private void OnLocalItemRightTapped(object sender, RightTappedRoutedEventArgs e) => SelectForContextMenu(LocalList, sender);

    private async void OnLocalTransferClick(object sender, RoutedEventArgs e) => await ViewModel.UploadAsync(Selection(LocalList));

    private async void OnLocalRenameClick(object sender, RoutedEventArgs e) =>
        await ViewModel.RenameLocalAsync(Selection(LocalList).FirstOrDefault());

    private async void OnLocalDeleteClick(object sender, RoutedEventArgs e) => await ViewModel.DeleteLocalAsync(Selection(LocalList));

    private async void OnLocalListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var selection = Selection(LocalList);
        switch (e.Key)
        {
            case VirtualKey.Enter when selection.Count == 1:
                e.Handled = true;
                ViewModel.OpenLocal(selection[0]);
                break;
            case VirtualKey.Back:
                e.Handled = true;
                ViewModel.LocalUpCommand.Execute(null);
                break;
            case VirtualKey.F5:
                e.Handled = true;
                ViewModel.RefreshLocalCommand.Execute(null);
                break;
            case VirtualKey.F2 when selection.Count == 1:
                e.Handled = true;
                await ViewModel.RenameLocalAsync(selection[0]);
                break;
            case VirtualKey.Delete when selection.Count > 0:
                e.Handled = true;
                await ViewModel.DeleteLocalAsync(selection);
                break;
            default:
                break;
        }
    }

    private void OnLocalDragItemsStarting(object sender, DragItemsStartingEventArgs e) =>
        StartDrag(DragSource.Local, e);

    private void OnLocalDragOver(object sender, DragEventArgs e)
    {
        // Seuls les éléments du panneau distant se déposent ici (= téléchargement).
        if (_dragSource == DragSource.Remote && ViewModel.IsConnected)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Loc.Get("Sftp_DropDownload");
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void OnLocalDrop(object sender, DragEventArgs e)
    {
        if (_dragSource == DragSource.Remote)
        {
            var items = _dragItems;
            EndDrag();
            await ViewModel.DownloadAsync(items);
        }
    }

    // ---- Panneau distant ---------------------------------------------------------------------

    private async void OnRemotePathKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await ViewModel.NavigateRemoteAsync(RemotePathBox.Text);
        }
    }

    private async void OnRemoteItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        await ViewModel.OpenRemoteAsync(ItemOf(sender));

    private void OnRemoteItemRightTapped(object sender, RightTappedRoutedEventArgs e) => SelectForContextMenu(RemoteList, sender);

    private async void OnRemoteTransferClick(object sender, RoutedEventArgs e) => await ViewModel.DownloadAsync(Selection(RemoteList));

    private async void OnRemoteEditClick(object sender, RoutedEventArgs e) =>
        await ViewModel.EditRemoteAsync(Selection(RemoteList).FirstOrDefault());

    private async void OnRemoteEditWithClick(object sender, RoutedEventArgs e) =>
        await ViewModel.EditRemoteAsync(Selection(RemoteList).FirstOrDefault(), chooseApplication: true);

    private async void OnRemoteRenameClick(object sender, RoutedEventArgs e) =>
        await ViewModel.RenameRemoteAsync(Selection(RemoteList).FirstOrDefault());

    private async void OnRemotePermissionsClick(object sender, RoutedEventArgs e) =>
        await ViewModel.EditPermissionsAsync(Selection(RemoteList).FirstOrDefault());

    private async void OnRemoteDeleteClick(object sender, RoutedEventArgs e) => await ViewModel.DeleteRemoteAsync(Selection(RemoteList));

    private async void OnRemoteListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var selection = Selection(RemoteList);
        switch (e.Key)
        {
            case VirtualKey.Enter when selection.Count == 1:
                e.Handled = true;
                await ViewModel.OpenRemoteAsync(selection[0]);
                break;
            case VirtualKey.Back:
                e.Handled = true;
                await ViewModel.RemoteUpCommand.ExecuteAsync(null);
                break;
            case VirtualKey.F5:
                e.Handled = true;
                await ViewModel.RefreshRemoteCommand.ExecuteAsync(null);
                break;
            case VirtualKey.F2 when selection.Count == 1:
                e.Handled = true;
                await ViewModel.RenameRemoteAsync(selection[0]);
                break;
            case VirtualKey.F4 when selection.Count == 1:
                // Comme WinSCP : F4 ouvre le fichier dans un éditeur.
                e.Handled = true;
                await ViewModel.EditRemoteAsync(selection[0]);
                break;
            case VirtualKey.Delete when selection.Count > 0:
                e.Handled = true;
                await ViewModel.DeleteRemoteAsync(selection);
                break;
            default:
                break;
        }
    }

    private void OnRemoteDragItemsStarting(object sender, DragItemsStartingEventArgs e) =>
        StartDrag(DragSource.Remote, e);

    private void OnRemoteDragOver(object sender, DragEventArgs e)
    {
        // Envoi : éléments du panneau local, ou fichiers glissés depuis l'Explorateur de Windows.
        var fromExplorer = _dragSource == DragSource.None && e.DataView.Contains(StandardDataFormats.StorageItems);
        if (ViewModel.IsConnected && (_dragSource == DragSource.Local || fromExplorer))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Loc.Get("Sftp_DropUpload");
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void OnRemoteDrop(object sender, DragEventArgs e)
    {
        if (_dragSource == DragSource.Local)
        {
            var items = _dragItems;
            EndDrag();
            await ViewModel.UploadAsync(items);
            return;
        }

        if (_dragSource == DragSource.None && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var deferral = e.GetDeferral();
            try
            {
                var storageItems = await e.DataView.GetStorageItemsAsync();
                var paths = storageItems.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
                await ViewModel.UploadAsync(paths);
            }
            finally
            {
                deferral.Complete();
            }
        }
    }

    // ---- Glisser-déposer interne ---------------------------------------------------------------

    private void StartDrag(DragSource source, DragItemsStartingEventArgs e)
    {
        var items = e.Items.OfType<FileItemViewModel>().Where(i => !i.IsParent).ToList();
        if (items.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        _dragSource = source;
        _dragItems = items;
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.SetText(string.Join(Environment.NewLine, items.Select(i => i.Name)));
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => EndDrag();

    private void EndDrag()
    {
        _dragSource = DragSource.None;
        _dragItems = [];
    }
}
