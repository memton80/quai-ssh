using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace Quai.App.Services;

/// <summary>
/// Diagnostic : note dans le journal chaque blocage du fil d'interface d'au moins une seconde (fenêtre qui ne répond
/// plus, saccades), avec le temps écoulé depuis le démarrage. Un minuteur de 250 ms qui arrive en retard mesure le
/// blocage ; aucune donnée ne quitte la machine.
/// </summary>
internal sealed class UiStallMonitor
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(1);

    // Au-delà, ce n'est pas l'interface qui a bloqué mais la machine qui dormait.
    private static readonly TimeSpan Sleep = TimeSpan.FromMinutes(1);

    private readonly ILogger _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private DispatcherQueueTimer? _timer;
    private TimeSpan _last;

    public UiStallMonitor(ILogger<UiStallMonitor> logger)
    {
        _logger = logger;
    }

    public void Start(DispatcherQueue queue)
    {
        _timer = queue.CreateTimer();
        _timer.Interval = Interval;
        _timer.Tick += OnTick;
        _last = _clock.Elapsed;
        _timer.Start();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        var now = _clock.Elapsed;
        var late = now - _last - Interval;
        _last = now;
        if (late >= Threshold && late < Sleep)
        {
            _logger.LogWarning(
                "Interface bloquée pendant {Milliseconds} ms ({Seconds} s après le démarrage)",
                (int)late.TotalMilliseconds,
                (int)(StartupClock.SinceProcessStart().TotalSeconds));
        }
    }
}
