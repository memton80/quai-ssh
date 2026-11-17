using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quai.App.Localization;
using Quai.Core.Models;
using Quai.Core.Ssh;

namespace Quai.App.Views;

/// <summary>Saisie d'un mot de passe ou d'une passphrase au moment de la connexion.</summary>
public sealed partial class SecretPromptDialog : ContentDialog
{
    public SecretPromptDialog(ConnectionProfile profile, SecretKind kind, bool isRetry)
    {
        InitializeComponent();

        var isPassword = kind == SecretKind.Password;
        Title = Loc.Get(isPassword ? "Secret_PasswordTitle" : "Secret_PassphraseTitle");
        MessageText.Text = isPassword
            ? Loc.Format("Secret_PasswordMessage", profile.Endpoint)
            : Loc.Format("Secret_PassphraseMessage", profile.PrivateKeyPath ?? string.Empty);
        SecretBox.PlaceholderText = Loc.Get(isPassword ? "Editor_Password" : "Editor_Passphrase");

        // Connexion rapide (hors carnet) : rien n'est mémorisé dans le coffre, le secret y resterait orphelin.
        if (profile.IsTransient)
        {
            RememberBox.IsChecked = false;
            RememberBox.Visibility = Visibility.Collapsed;
        }

        if (isRetry)
        {
            RetryInfo.Message = Loc.Get(isPassword ? "Secret_PasswordRejected" : "Secret_PassphraseRejected");
            RetryInfo.Visibility = Visibility.Visible;
        }

        Opened += (_, _) => SecretBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Le secret saisi ; il ne quitte la mémoire que vers le coffre Windows, si la case est cochée.</summary>
    public SecretInput Result => new(SecretBox.Password, RememberBox.IsChecked == true);

    private void OnSecretChanged(object sender, RoutedEventArgs e) =>
        IsPrimaryButtonEnabled = SecretBox.Password.Length > 0;
}
