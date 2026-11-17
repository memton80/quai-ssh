using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quai.Core.ViewModels;

namespace Quai.App.Views;

/// <summary>Fiche de création / modification d'un snippet.</summary>
public sealed partial class SnippetEditorDialog : ContentDialog
{
    public SnippetEditorDialog(SnippetEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    public SnippetEditorViewModel ViewModel { get; }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // La fiche reste ouverte tant que la commande est vide ; l'erreur s'affiche en haut.
        if (!ViewModel.Validate())
        {
            args.Cancel = true;
        }
    }
}
