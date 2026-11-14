using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quai.Core.ViewModels;

namespace Quai.App.Views;

/// <summary>Fiche de création / modification d'une connexion.</summary>
public sealed partial class ConnectionEditorDialog : ContentDialog
{
    private readonly Func<Task<string?>> _pickPrivateKey;

    public ConnectionEditorDialog(ConnectionEditorViewModel viewModel, Func<Task<string?>> pickPrivateKey)
    {
        ViewModel = viewModel;
        _pickPrivateKey = pickPrivateKey;
        InitializeComponent();
    }

    public ConnectionEditorViewModel ViewModel { get; }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // La fiche reste ouverte tant que la saisie est invalide ; l'erreur s'affiche en haut.
        if (!ViewModel.Validate())
        {
            args.Cancel = true;
        }
    }

    private async void OnBrowseKeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await _pickPrivateKey();
            if (!string.IsNullOrEmpty(path))
            {
                ViewModel.PrivateKeyPath = path;
            }
        }
        catch (Exception)
        {
            // Sélecteur indisponible : le chemin reste saisissable à la main.
        }
    }

    /// <summary>Test de fumée : textes réellement affichés par les champs du port série.</summary>
    internal (string Port, string BaudRate) SerialTextForTest => (SerialPortBox.Text, BaudRateBox.Text);

    private static void ShowSuggestions(AutoSuggestBox box, IEnumerable<string> values, string? text)
    {
        var query = text?.Trim() ?? string.Empty;
        box.ItemsSource = values.Where(v => v.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // À l'entrée dans le champ, toute la liste ; à la frappe, ce qui correspond.
    private void OnSerialPortGotFocus(object sender, RoutedEventArgs e) => ShowSuggestions(SerialPortBox, ViewModel.SerialPorts, null);

    private void OnSerialPortTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ShowSuggestions(sender, ViewModel.SerialPorts, sender.Text);
        }
    }

    private void OnBaudRateGotFocus(object sender, RoutedEventArgs e) => ShowSuggestions(BaudRateBox, ViewModel.BaudRates, null);

    private void OnBaudRateTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ShowSuggestions(sender, ViewModel.BaudRates, sender.Text);
        }
    }

    private void OnFolderGotFocus(object sender, RoutedEventArgs e) => UpdateFolderSuggestions(FolderBox.Text);

    private void OnFolderTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            UpdateFolderSuggestions(sender.Text);
        }
    }

    private void UpdateFolderSuggestions(string? text)
    {
        var query = text?.Trim() ?? string.Empty;
        FolderBox.ItemsSource = ViewModel.KnownFolders
            .Where(f => f.Contains(query, StringComparison.CurrentCultureIgnoreCase) && !f.Equals(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }
}
