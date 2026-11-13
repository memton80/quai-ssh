using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quai.App.Localization;
using Quai.Core.Ssh;

namespace Quai.App.Views;

/// <summary>
/// Décision sur une clé d'hôte : premier contact (faire confiance ?) ou empreinte modifiée (alerte).
/// </summary>
public sealed partial class HostKeyDialog : ContentDialog
{
    /// <param name="presented">Clé présentée par le serveur.</param>
    /// <param name="known">Clés déjà enregistrées pour cet hôte ; <c>null</c> ou vide = premier contact.</param>
    public HostKeyDialog(HostKeyInfo presented, IReadOnlyList<HostKeyInfo>? known)
    {
        InitializeComponent();

        PresentedLabel.Text = Loc.Format("HostKey_PresentedFingerprint", presented.KeyType);
        PresentedFingerprintText.Text = presented.Fingerprint;

        if (known is null || known.Count == 0)
        {
            // Premier contact : l'utilisateur vérifie l'empreinte puis choisit de faire confiance.
            Title = Loc.Get("HostKey_UnknownTitle");
            PrimaryButtonText = Loc.Get("HostKey_Trust");
            CloseButtonText = Loc.Get("Common_Cancel");
            DefaultButton = ContentDialogButton.Primary;
            MessageText.Text = Loc.Format("HostKey_UnknownMessage", presented.Endpoint);
            AdviceText.Text = Loc.Get("HostKey_UnknownAdvice");
            return;
        }

        // Empreinte modifiée : alerte explicite, « Annuler » par défaut ; remplacer la clé enregistrée
        // reste possible mais demande un geste volontaire.
        Title = Loc.Get("HostKey_ChangedTitle");
        PrimaryButtonText = Loc.Get("HostKey_Replace");
        CloseButtonText = Loc.Get("HostKey_Abort");
        DefaultButton = ContentDialogButton.Close;
        ChangedAlert.Visibility = Visibility.Visible;
        MessageText.Text = Loc.Format("HostKey_ChangedMessage", presented.Endpoint);
        KnownPanel.Visibility = Visibility.Visible;
        KnownFingerprintText.Text = string.Join(Environment.NewLine, known.Select(k => $"{k.Fingerprint} ({k.KeyType})"));
        AdviceText.Text = Loc.Get("HostKey_ChangedAdvice");
    }

    public static HostKeyDialog ForUnknownHost(HostKeyInfo presented) => new(presented, null);

    public static HostKeyDialog ForChangedHost(HostKeyInfo presented, IReadOnlyList<HostKeyInfo> known) => new(presented, known);
}
