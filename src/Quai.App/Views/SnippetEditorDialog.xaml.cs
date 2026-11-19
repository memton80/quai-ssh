using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Quai.Core.ViewModels;
using Windows.System;
using Windows.UI.Core;

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

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // La fiche reste ouverte tant que la commande est vide ou le raccourci refusé ; l'erreur s'affiche en haut.
        if (!ViewModel.Validate())
        {
            args.Cancel = true;
        }
    }

    /// <summary>
    /// Combinaison appuyée dans le champ du raccourci. Restent à la fiche : Tab (champ suivant), Échap (fermer),
    /// Entrée (enregistrer) et Alt+F4 ; Retour arrière ou Suppr seuls effacent le raccourci.
    /// </summary>
    private void OnShortcutPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key;
        var control = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);
        var alt = IsDown(VirtualKey.Menu);

        if (key is VirtualKey.Tab or VirtualKey.Escape or VirtualKey.Enter || (alt && key == VirtualKey.F4))
        {
            return;
        }

        e.Handled = true;
        if (key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
            or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            // Modificateur seul : on attend la touche.
            return;
        }

        if (key is VirtualKey.Back or VirtualKey.Delete && !control && !shift && !alt)
        {
            ViewModel.ClearShortcut();
            return;
        }

        ViewModel.CaptureShortcut(control, shift, alt, (int)key);
    }

    private void OnClearShortcutClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearShortcut();
        ShortcutBox.Focus(FocusState.Programmatic);
    }
}
