using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Quai.App.Localization;
using Quai.Core.ViewModels;
using Windows.System;

namespace Quai.App.Views;

/// <summary>Choix d'un snippet à envoyer dans le terminal (<c>Ctrl+Maj+P</c>).</summary>
public sealed partial class SnippetPickerDialog : ContentDialog
{
    public SnippetPickerDialog(SnippetPickerViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Title = Loc.Format("Snippets_PickerTitle", viewModel.Target);
        UpdatePrimary();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SnippetPickerViewModel.Selected))
            {
                UpdatePrimary();
                if (ViewModel.Selected is { } selected)
                {
                    SnippetList.ScrollIntoView(selected);
                }
            }
        };
        Opened += (_, _) => FilterBox.Focus(FocusState.Programmatic);
    }

    public SnippetPickerViewModel ViewModel { get; }

    /// <summary>Envoyer, ou rien (fermé avec le bouton « Annuler » ou Échap).</summary>
    public bool SendRequested { get; private set; }

    /// <summary>Test de fumée : envoie le snippet sélectionné comme le ferait Entrée.</summary>
    internal void SendForTest()
    {
        SendRequested = ViewModel.Selected is not null;
        Hide();
    }

    private void UpdatePrimary() => IsPrimaryButtonEnabled = ViewModel.Selected is not null;

    private void OnFilterPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                ViewModel.Move(1);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                ViewModel.Move(-1);
                e.Handled = true;
                break;
            case VirtualKey.Enter when ViewModel.Selected is not null:
                SendRequested = true;
                e.Handled = true;
                Hide();
                break;
            default:
                break;
        }
    }

    private void OnListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.Selected is not null)
        {
            SendRequested = true;
            Hide();
        }
    }
}
