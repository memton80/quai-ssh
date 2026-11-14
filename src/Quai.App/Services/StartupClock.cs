using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace Quai.App.Services;

/// <summary>Durée de démarrage : du lancement du processus au premier rendu de la fenêtre principale.</summary>
internal static class StartupClock
{
    private static EventHandler<object>? _handler;

    /// <summary>Durée mesurée, ou <c>null</c> tant que la fenêtre n'a pas été rendue.</summary>
    public static TimeSpan? FirstFrame { get; private set; }

    /// <summary>Temps écoulé depuis le lancement du processus.</summary>
    public static TimeSpan SinceProcessStart()
    {
        using var process = Process.GetCurrentProcess();
        return DateTime.Now - process.StartTime;
    }

    /// <summary>À appeler juste après l'activation de la fenêtre : relève la durée au premier rendu.</summary>
    public static void TrackFirstFrame(Action<TimeSpan> onFirstFrame)
    {
        _handler = (_, _) =>
        {
            CompositionTarget.Rendering -= _handler;
            _handler = null;
            FirstFrame = SinceProcessStart();
            onFirstFrame(FirstFrame.Value);
        };
        CompositionTarget.Rendering += _handler;
    }
}
