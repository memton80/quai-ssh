using System.Diagnostics;
using System.Globalization;
using System.Text;
using Quai.App.Services;

namespace Quai.App;

/// <summary>
/// Mesures du test de fumée (démarrage, mémoire, fils, handles) à plusieurs moments du parcours, écrites dans
/// <c>smoke-metrics.md</c> et reprises dans le résumé du CI : la référence pour juger les optimisations.
/// </summary>
internal sealed class SmokeMetrics
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly List<string> _rows = [];

    /// <summary>Relève l'état du processus, après un ramasse-miettes complet pour des chiffres comparables.</summary>
    public void Snapshot(string label)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var process = Process.GetCurrentProcess();
        _rows.Add(string.Create(
            French,
            $"| {label} | {Mb(process.WorkingSet64)} | {Mb(process.PrivateMemorySize64)} | {Mb(GC.GetTotalMemory(false))} | {process.Threads.Count} | {process.HandleCount} |"));
    }

    public string ToMarkdown()
    {
        var text = new StringBuilder();
        text.AppendLine("### Test de fumée : démarrage et ressources");
        text.AppendLine();
        var firstFrame = StartupClock.FirstFrame is { } elapsed
            ? string.Create(French, $"{elapsed.TotalMilliseconds:N0} ms")
            : "non mesuré";
        text.AppendLine($"Fenêtre affichée (lancement du processus → premier rendu) : **{firstFrame}**");
        text.AppendLine();
        text.AppendLine("| Moment | Mémoire physique (Mo) | Mémoire privée (Mo) | Tas .NET (Mo) | Fils | Handles |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var row in _rows)
        {
            text.AppendLine(row);
        }

        return text.ToString();
    }

    private static string Mb(long bytes) => (bytes / 1024d / 1024d).ToString("N1", French);
}
