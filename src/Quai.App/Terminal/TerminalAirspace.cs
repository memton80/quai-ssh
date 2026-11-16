using Windows.Foundation;

namespace Quai.App.Terminal;

/// <summary>
/// Le terminal est une fenêtre Win32 posée au-dessus du XAML : tout élément XAML qui doit passer devant lui
/// doit être connu. Les menus et info-bulles (popups) sont détectés par la vue du terminal ; les dialogues
/// et le volet de navigation déplié en superposition sont signalés ici.
/// </summary>
internal static class TerminalAirspace
{
    private static int _modalDialogs;

    /// <summary>Un recouvrement a changé : les terminaux recalculent leur zone visible.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Le contenu glisse en animation (volet de navigation qui s'ouvre ou se ferme à côté du contenu) : une animation
    /// ne relance pas la mise en page, les terminaux suivent donc image par image le temps qu'elle dure.
    /// </summary>
    public static event Action? Moving;

    /// <summary>Rectangles (coordonnées de la racine XAML) actuellement posés au-dessus du contenu.</summary>
    public static Func<IEnumerable<Rect>>? OverlayProvider { get; set; }

    /// <summary>Un dialogue modal recouvre toute la fenêtre : les terminaux s'effacent.</summary>
    public static bool IsModalOpen => _modalDialogs > 0;

    public static IEnumerable<Rect> Overlays() => OverlayProvider?.Invoke() ?? [];

    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>Suivi des animations du volet (désactivé seulement par le test de fumée, pour constater le bug sans lui).</summary>
    public static bool FollowAnimations { get; set; } = true;

    public static void NotifyMoving()
    {
        if (FollowAnimations)
        {
            Moving?.Invoke();
        }
    }

    /// <summary>Fin d'une animation du volet : dernier recalage des terminaux.</summary>
    public static void NotifyMoved()
    {
        if (FollowAnimations)
        {
            Changed?.Invoke();
        }
    }

    public static void BeginModal()
    {
        _modalDialogs++;
        NotifyChanged();
    }

    public static void EndModal()
    {
        _modalDialogs = Math.Max(0, _modalDialogs - 1);
        NotifyChanged();
    }
}
