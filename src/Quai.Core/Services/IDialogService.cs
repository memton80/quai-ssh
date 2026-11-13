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
}
