using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Models;

namespace Quai.Core.Local;

public enum ElevatedLaunchResult
{
    Started,

    /// <summary>« Non » à la demande de Windows (UAC) : rien n'est lancé, rien à signaler.</summary>
    Cancelled,

    Failed,
}

/// <summary>Ouvre un shell administrateur dans sa propre fenêtre, après confirmation de Windows.</summary>
public interface IElevatedShellLauncher
{
    /// <param name="error">Message de l'échec (<see cref="ElevatedLaunchResult.Failed"/>), sinon vide.</param>
    ElevatedLaunchResult Launch(LocalShell shell, out string error);
}

/// <summary>
/// Lancement par Windows (verbe <c>runas</c> de ShellExecute) : l'UAC présente le shell lui-même (« Microsoft Windows,
/// éditeur vérifié »), et Windows l'ouvre dans le terminal par défaut (Windows Terminal sous Windows 11). Aucun code de
/// Quai ne s'exécute avec les droits d'administrateur : Quai s'installe dans un dossier modifiable sans droits, et
/// le relancer élevé donnerait ces droits à quiconque peut remplacer ses fichiers.
/// </summary>
public sealed class ElevatedShellLauncher : IElevatedShellLauncher
{
    /// <summary>ERROR_CANCELLED : refus à la demande de confirmation de Windows.</summary>
    private const int ErrorCancelled = 1223;

    private readonly Func<ProcessStartInfo, Process?> _start;
    private readonly ILogger _logger;

    public ElevatedShellLauncher(ILogger<ElevatedShellLauncher>? logger = null)
        : this(Process.Start, logger)
    {
    }

    internal ElevatedShellLauncher(Func<ProcessStartInfo, Process?> start, ILogger? logger = null)
    {
        _start = start;
        _logger = logger ?? NullLogger.Instance;
    }

    public ElevatedLaunchResult Launch(LocalShell shell, out string error)
    {
        ArgumentNullException.ThrowIfNull(shell);
        error = string.Empty;

        // Seuls les shells de la liste administrateur (dossiers protégés) sont acceptés, jamais un chemin quelconque.
        if (!shell.Id.StartsWith(LocalShellCatalog.AdminPrefix, StringComparison.Ordinal) || !LocalShellCatalog.IsLocalDrivePath(shell.Path))
        {
            throw new ArgumentException("Shell non prévu pour l'administrateur.", nameof(shell));
        }

        var info = new ProcessStartInfo(shell.Path)
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        foreach (var argument in shell.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = _start(info);
            _logger.LogInformation("Shell administrateur demandé : {Shell}", shell.Id);
            return ElevatedLaunchResult.Started;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            _logger.LogInformation("Shell administrateur refusé à la confirmation de Windows : {Shell}", shell.Id);
            return ElevatedLaunchResult.Cancelled;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            _logger.LogWarning("Lancement du shell administrateur {Shell} impossible ({Error})", shell.Id, ex.GetType().Name);
            error = ex.Message;
            return ElevatedLaunchResult.Failed;
        }
    }
}
