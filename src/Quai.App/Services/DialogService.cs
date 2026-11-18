using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quai.App.Localization;
using Quai.App.Terminal;
using Quai.App.Views;
using Quai.Core.Models;
using Quai.Core.Services;
using Quai.Core.Ssh;
using Quai.Core.ViewModels;

namespace Quai.App.Services;

/// <summary>
/// Boîtes de dialogue WinUI : une seule à la fois (contrainte de <see cref="ContentDialog"/>),
/// toujours affichées depuis le fil d'interface quel que soit le fil appelant.
/// </summary>
internal sealed class DialogService : IDialogService, IHostKeyPrompt, ICredentialPrompt
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Racine de la fenêtre principale ; renseignée par celle-ci à sa création.</summary>
    public FrameworkElement? Root { get; set; }

    /// <summary>Identifiant de la fenêtre (pour les sélecteurs de fichiers).</summary>
    public Microsoft.UI.WindowId WindowId { get; set; }

    /// <summary>Dialogue actuellement affiché (utilisé par le test de fumée).</summary>
    public ContentDialog? Current { get; private set; }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false) =>
        OnUiAsync(async () =>
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
        });

    public Task ShowErrorAsync(string title, string message) =>
        OnUiAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                CloseButtonText = Loc.Get("Common_Close"),
                DefaultButton = ContentDialogButton.Close,
            };

            await ShowAsync(dialog);
            return true;
        });

    /// <summary>Message d'information (titre, texte sélectionnable, bouton « Fermer »).</summary>
    public Task ShowInfoAsync(string title, string message) => ShowErrorAsync(title, message);

    /// <summary>Formulaire « idée ou bug » ; retourne vrai si l'utilisateur continue vers GitHub (formulaire valide).</summary>
    public Task<bool> ShowFeedbackAsync(FeedbackViewModel feedback, string logsDirectory) =>
        OnUiAsync(async () => await ShowAsync(new FeedbackDialog(feedback, logsDirectory)) == ContentDialogResult.Primary);

    public Task<bool> EditConnectionAsync(ConnectionEditorViewModel editor) =>
        OnUiAsync(async () =>
            await ShowAsync(new ConnectionEditorDialog(editor, PickPrivateKeyAsync)) == ContentDialogResult.Primary);

    public Task<string?> PromptTextAsync(string title, string label, string initialValue, string confirmText) =>
        OnUiAsync(async () =>
        {
            var dialog = new TextPromptDialog(title, label, initialValue, confirmText);
            return await ShowAsync(dialog) == ContentDialogResult.Primary ? dialog.Value : null;
        });

    public Task<bool> EditPermissionsAsync(PermissionsEditorViewModel editor) =>
        OnUiAsync(async () => await ShowAsync(new PermissionsDialog(editor)) == ContentDialogResult.Primary);

    public Task<bool> EditSnippetAsync(SnippetEditorViewModel editor) =>
        OnUiAsync(async () => await ShowAsync(new SnippetEditorDialog(editor)) == ContentDialogResult.Primary);

    public Task<SnippetPickerResult> PickSnippetAsync(SnippetPickerViewModel picker) =>
        OnUiAsync(async () =>
        {
            var dialog = new SnippetPickerDialog(picker);
            return await ShowAsync(dialog) switch
            {
                ContentDialogResult.Primary => SnippetPickerResult.Send,
                ContentDialogResult.Secondary => SnippetPickerResult.Manage,

                // Entrée dans le champ de recherche ou double-clic : le dialogue se ferme lui-même.
                _ => dialog.SendRequested ? SnippetPickerResult.Send : SnippetPickerResult.Cancelled,
            };
        });

    public Task<bool> ConfirmUnknownHostAsync(HostKeyInfo presented) =>
        OnUiAsync(async () =>
            await ShowAsync(HostKeyDialog.ForUnknownHost(presented)) == ContentDialogResult.Primary);

    public Task<bool> ConfirmChangedHostAsync(HostKeyInfo presented, IReadOnlyList<HostKeyInfo> known) =>
        OnUiAsync(async () =>
            await ShowAsync(HostKeyDialog.ForChangedHost(presented, known)) == ContentDialogResult.Primary);

    public Task<SecretInput?> PromptSecretAsync(ConnectionProfile profile, SecretKind kind, bool isRetry) =>
        OnUiAsync(async () =>
        {
            var dialog = new SecretPromptDialog(profile, kind, isRetry);
            return await ShowAsync(dialog) == ContentDialogResult.Primary ? dialog.Result : null;
        });

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

    private Task<T> OnUiAsync<T>(Func<Task<T>> action)
    {
        var queue = (Root ?? throw new InvalidOperationException("La fenêtre principale n'est pas prête.")).DispatcherQueue;
        if (queue.HasThreadAccess)
        {
            return action();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = queue.TryEnqueue(async () =>
        {
            try
            {
                completion.TrySetResult(await action());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        if (!queued)
        {
            completion.TrySetException(new InvalidOperationException("Le fil d'interface n'accepte plus de travail."));
        }

        return completion.Task;
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

            // Les terminaux sont des fenêtres natives posées sur le XAML : ils s'effacent derrière le dialogue.
            Current = dialog;
            TerminalAirspace.BeginModal();
            try
            {
                return await dialog.ShowAsync();
            }
            finally
            {
                TerminalAirspace.EndModal();
            }
        }
        finally
        {
            Current = null;
            _gate.Release();
        }
    }
}
