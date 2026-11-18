using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quai.Core.ViewModels;

namespace Quai.App.Views;

/// <summary>Formulaire « Proposer une idée ou signaler un bug » (la publication se fait ensuite sur GitHub).</summary>
public sealed partial class FeedbackDialog : ContentDialog
{
    private readonly string _logsDirectory;

    public FeedbackDialog(FeedbackViewModel viewModel, string logsDirectory)
    {
        ViewModel = viewModel;
        _logsDirectory = logsDirectory;
        InitializeComponent();
        Opened += (_, _) => TitleBox.Focus(FocusState.Programmatic);
    }

    public FeedbackViewModel ViewModel { get; }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Le formulaire reste ouvert tant que le titre ou la description manque ; l'erreur s'affiche en haut.
        if (!ViewModel.Validate())
        {
            args.Cancel = true;
        }
    }

    /// <summary>Explorateur ouvert sur le dossier des journaux, le plus récent sélectionné (prêt à glisser sur GitHub).</summary>
    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var latest = new DirectoryInfo(_logsDirectory).EnumerateFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            var arguments = latest is null ? $"\"{_logsDirectory}\"" : $"/select,\"{latest.FullName}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Dossier inaccessible : rien à ouvrir.
        }
    }
}
