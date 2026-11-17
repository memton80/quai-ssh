namespace Quai.Core.Ssh;

/// <summary>
/// Dossier courant du shell interactif d'une connexion, lu sans rien taper dans le terminal : une commande est
/// exécutée sur un canal à part de la même connexion SSH. OpenSSH lance tous les canaux d'une connexion depuis
/// le même processus <c>sshd</c> : le shell interactif est donc un « frère » de la commande, le seul doté d'un
/// terminal. On lit le dossier du programme au premier plan de ce terminal (un sous-shell, <c>vim</c>…), sinon
/// celui du shell, dans <c>/proc</c> (Linux). Ailleurs (BSD, macOS) ou sans droits (sudo) : aucun résultat.
/// </summary>
internal static class WorkingDirectoryProbe
{
    public const string Marker = "QUAI_CWD=";

    /// <summary>
    /// <c>exec sh -c</c> : la commande passe par le shell de connexion de l'utilisateur (bash, zsh, fish…), qui
    /// cède la place à <c>sh</c> ; le parent de <c>sh</c> est alors bien le processus <c>sshd</c> de la connexion.
    /// </summary>
    public const string Command =
        "exec sh -c 'for p in $(ps -o pid= --ppid $PPID 2>/dev/null); do [ \"$p\" = \"$$\" ] && continue; "
        + "case \"$(ps -o tty= -p $p 2>/dev/null)\" in \"\"|*\"?\"*) continue;; esac; "
        + "for c in $(ps -o tpgid= -p $p 2>/dev/null) $p; do d=$(readlink /proc/$c/cwd 2>/dev/null) "
        + "&& [ -n \"$d\" ] && { echo \"" + Marker + "$d\"; exit 0; }; done; done; exit 1'";

    /// <summary>Chemin annoncé par la commande (dernière ligne marquée), ou <c>null</c>.</summary>
    public static string? Parse(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        var line = output
            .Split('\n', StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.StartsWith(Marker, StringComparison.Ordinal));
        var path = line?[Marker.Length..];
        return path is { Length: > 0 } && path.StartsWith('/') && !path.Any(char.IsControl) ? path : null;
    }
}
