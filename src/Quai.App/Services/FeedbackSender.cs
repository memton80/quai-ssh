using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quai.App.Localization;
using Quai.Core.Feedback;
using Quai.Core.Localization;
using Quai.Core.Storage;
using Quai.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Quai.App.Services;

/// <summary>
/// Bouton « Idée ou bug » : formulaire dans Quai, puis page GitHub d'une nouvelle issue déjà remplie dans le navigateur.
/// Quai n'envoie rien lui-même (pas de serveur, pas de jeton) : l'utilisateur relit et publie sur GitHub.
/// </summary>
internal sealed class FeedbackSender(IStrings strings, DialogService dialogs, AppPaths paths, ILogger<FeedbackSender> logger)
{
    public async Task RunAsync()
    {
        var feedback = new FeedbackViewModel(strings, FeedbackIssue.DescribeSystem());
        if (!await dialogs.ShowFeedbackAsync(feedback, paths.LogsDirectory))
        {
            return;
        }

        var link = feedback.BuildLink();
        if (!OpenInBrowser(link.Address))
        {
            CopyToClipboard(link.FullText);
            await dialogs.ShowErrorAsync(
                Loc.Get("Feedback_BrowserFailedTitle"),
                Loc.Format("Feedback_BrowserFailedMessage", FeedbackIssue.NewIssuePage.AbsoluteUri));
            return;
        }

        if (link.Shortened)
        {
            // Adresse limitée par Windows : la fin du texte manque sur GitHub, le texte complet attend dans le presse-papiers.
            CopyToClipboard(link.FullText);
            await dialogs.ShowInfoAsync(Loc.Get("Feedback_ShortenedTitle"), Loc.Get("Feedback_ShortenedMessage"));
        }
    }

    private bool OpenInBrowser(Uri address)
    {
        try
        {
            // Navigateur par défaut ; l'adresse est construite par l'application (page « nouvelle issue » du dépôt de Quai).
            Process.Start(new ProcessStartInfo(address.OriginalString) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Ouverture du navigateur impossible ({Error})", ex.GetType().Name);
            return false;
        }
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);

            // Le texte reste disponible même si Quai est fermé avant de le coller.
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            // Presse-papiers verrouillé par une autre application.
            logger.LogWarning("Copie dans le presse-papiers impossible ({Error})", ex.GetType().Name);
        }
    }
}
