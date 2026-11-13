using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quai.App.Localization;
using Quai.App.Views;
using Quai.Core.Services;
using Quai.Core.ViewModels;

namespace Quai.App.Services;

/// <summary>Boîtes de dialogue WinUI (une seule à la fois, comme l'exige <see cref="ContentDialog"/>).</summary>
internal sealed class DialogService : IDialogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Racine de la fenêtre principale ; renseignée par celle-ci à sa création.</summary>
    public FrameworkElement? Root { get; set; }

    /// <summary>Identifiant de la fenêtre (pour les sélecteurs de fichiers).</summary>
    public Microsoft.UI.WindowId WindowId { get; set; }

    /// <summary>Dialogue actuellement affiché (utilisé par le test de fumée).</summary>
    public ContentDialog? Current { get; private set; }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = Loc.Get("Common_Cancel"),
            // Action irréversible : « Annuler » reste le bouton par défaut.
            DefaultButton = isDestructive ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };

        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            CloseButtonText = Loc.Get("Common_Close"),
            DefaultButton = ContentDialogButton.Close,
        };

        await ShowAsync(dialog);
    }

    public async Task<bool> EditConnectionAsync(ConnectionEditorViewModel editor)
    {
        var dialog = new ConnectionEditorDialog(editor, PickPrivateKeyAsync);
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    /// <summary>Sélecteur de fichier pour la clé privée ; retourne <c>null</c> si l'utilisateur annule.</summary>
    public async Task<string?> PickPrivateKeyAsync()
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(WindowId);
        picker.FileTypeFilter.Add("*");

        var sshFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        if (Directory.Exists(sshFolder))
        {
            picker.SuggestedFolder = sshFolder;
        }

        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        var root = Root ?? throw new InvalidOperationException("La fenêtre principale n'est pas prête.");

        await _gate.WaitAsync();
        try
        {
            dialog.XamlRoot = root.XamlRoot;
            dialog.RequestedTheme = root.ActualTheme;
            if (dialog.Style is null && Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style))
            {
                dialog.Style = style as Style;
            }

            Current = dialog;
            return await dialog.ShowAsync();
        }
        finally
        {
            Current = null;
            _gate.Release();
        }
    }
}
