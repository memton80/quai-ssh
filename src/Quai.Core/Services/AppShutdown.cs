using Quai.Core.Localization;
using Quai.Core.ViewModels;

namespace Quai.Core.Services;

/// <summary>Connexions encore actives au moment de quitter l'application.</summary>
/// <param name="UnsentEdits">Fichiers distants modifiés dans un éditeur dont la dernière version n'est pas sur le serveur.</param>
public sealed record OpenSessions(int Terminals, int SftpSessions, int ActiveTransfers, int UnsentEdits = 0)
{
    public bool IsEmpty => Terminals == 0 && SftpSessions == 0 && ActiveTransfers == 0 && UnsentEdits == 0;

    /// <summary>Quitter ferait perdre quelque chose (transfert interrompu, modification jamais renvoyée).</summary>
    public bool LosesWork => ActiveTransfers > 0 || UnsentEdits > 0;
}

/// <summary>
/// Fermeture de l'application : inventaire de ce qui est encore ouvert (pour la confirmation),
/// puis déconnexion propre de chaque session, transferts interrompus et fichiers incomplets supprimés.
/// </summary>
public sealed class AppShutdown
{
    private readonly TerminalsViewModel _terminals;
    private readonly SftpSessionsViewModel _sftp;
    private readonly IStrings _strings;

    public AppShutdown(TerminalsViewModel terminals, SftpSessionsViewModel sftp, IStrings strings)
    {
        _terminals = terminals;
        _sftp = sftp;
        _strings = strings;
    }

    /// <summary>Sessions connectées ou en cours de connexion, transferts en attente ou en cours.</summary>
    public OpenSessions Inventory() => new(
        _terminals.Tabs.Count(t => t.IsConnected || t.IsConnecting),
        _sftp.Tabs.Count(t => t.IsConnected || t.IsConnecting),
        _sftp.Tabs.Sum(t => t.Transfers.Count(transfer => transfer.IsActive)),
        _sftp.Tabs.Sum(t => t.UnsentEdits.Count));

    /// <summary>Texte de la confirmation : ce qui sera fermé, et ce qui sera interrompu.</summary>
    public string Describe(OpenSessions open)
    {
        var parts = new List<string>();
        if (open.Terminals > 0)
        {
            parts.Add(open.Terminals == 1 ? _strings.Get("Shutdown_OneTerminal") : _strings.Format("Shutdown_Terminals", open.Terminals));
        }

        if (open.SftpSessions > 0)
        {
            parts.Add(open.SftpSessions == 1 ? _strings.Get("Shutdown_OneSftp") : _strings.Format("Shutdown_Sftp", open.SftpSessions));
        }

        var lines = new List<string>();
        if (parts.Count > 0)
        {
            var list = parts.Count == 1 ? parts[0] : _strings.Format("Shutdown_Both", parts[0], parts[1]);
            lines.Add(_strings.Format("Shutdown_SessionsOpen", list));
        }

        if (open.ActiveTransfers > 0)
        {
            lines.Add(open.ActiveTransfers == 1
                ? _strings.Get("Shutdown_OneTransfer")
                : _strings.Format("Shutdown_Transfers", open.ActiveTransfers));
        }

        if (open.UnsentEdits > 0)
        {
            lines.Add(open.UnsentEdits == 1
                ? _strings.Get("Shutdown_OneUnsentEdit")
                : _strings.Format("Shutdown_UnsentEdits", open.UnsentEdits));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    /// <summary>
    /// Ferme tous les onglets ; rend la main après <paramref name="timeout"/> au plus, même si un serveur ne
    /// répond plus (Windows coupera alors la connexion à la sortie du processus).
    /// </summary>
    /// <returns><c>true</c> si tout a été fermé dans le délai.</returns>
    public async Task<bool> CloseAllAsync(TimeSpan timeout)
    {
        var closing = Task.WhenAll(_terminals.CloseAllAsync(), _sftp.CloseAllAsync());
        return await Task.WhenAny(closing, Task.Delay(timeout)) == closing;
    }
}
