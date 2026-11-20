using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quai.App.Localization;
using Quai.Core;
using Quai.Core.ViewModels;

namespace Quai.App.Views;

/// <summary>Réglages : thème, police et taille du terminal. Tout est appliqué immédiatement.</summary>
public sealed partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    public string VersionText => AppInfo.DisplayName + " — " + Loc.Format("Settings_Version", ViewModel.Version);

    private void OnFontGotFocus(object sender, RoutedEventArgs e) => UpdateFontSuggestions(string.Empty);

    private void OnFontTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            UpdateFontSuggestions(sender.Text);
        }
    }

    private void UpdateFontSuggestions(string? text)
    {
        var query = text?.Trim() ?? string.Empty;
        FontBox.ItemsSource = ViewModel.SuggestedFonts
            .Where(f => f.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    private async void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchFolderPathAsync(ViewModel.DataDirectory);
        }
        catch (Exception)
        {
            // Dossier inaccessible : rien à ouvrir.
        }
    }
}
