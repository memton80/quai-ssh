using Quai.Core.ViewModels;

namespace Quai.Core.Services;

/// <summary>Boîtes de dialogue de l'application, vues depuis les view-models (implémentées par l'interface WinUI).</summary>
public interface IDialogService
{
    /// <summary>Demande une confirmation ; <paramref name="isDestructive"/> signale une action irréversible.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false);

    Task ShowErrorAsync(string title, string message);

    /// <summary>Ouvre la fiche d'une connexion ; retourne vrai si l'utilisateur a validé.</summary>
    Task<bool> EditConnectionAsync(ConnectionEditorViewModel editor);

    /// <summary>Demande un texte court (nom de fichier ou de dossier) ; <c>null</c> si l'utilisateur annule.</summary>
    Task<string?> PromptTextAsync(string title, string label, string initialValue, string confirmText);

    /// <summary>Ouvre l'éditeur de droits ; retourne vrai si l'utilisateur a validé.</summary>
    Task<bool> EditPermissionsAsync(PermissionsEditorViewModel editor);

    /// <summary>Ouvre la fiche d'un snippet ; retourne vrai si l'utilisateur a validé.</summary>
    Task<bool> EditSnippetAsync(SnippetEditorViewModel editor);

    /// <summary>Choix d'un snippet à envoyer (<see cref="SnippetPickerViewModel.Selected"/>).</summary>
    Task<SnippetPickerResult> PickSnippetAsync(SnippetPickerViewModel picker);
}

public enum SnippetPickerResult
{
    Cancelled,

    /// <summary>Envoyer le snippet choisi.</summary>
    Send,

    /// <summary>Ouvrir la page des snippets.</summary>
    Manage,
}
