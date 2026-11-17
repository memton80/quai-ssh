using System.ComponentModel;
using System.Diagnostics;
using Quai.Core.Services;

namespace Quai.App.Services;

/// <summary>
/// Ouvre un fichier dans l'application que Windows lui associe, ou avec la fenêtre « Ouvrir avec » de Windows
/// (choix demandé, ou type de fichier sans application associée : <c>nginx.conf</c>, <c>.bashrc</c>…).
/// </summary>
internal sealed class ShellFileLauncher : IFileLauncher
{
    // ShellExecuteEx : aucune application associée à ce type de fichier.
    private const int ErrorNoAssociation = 1155;

    public Task<bool> OpenAsync(string path, bool chooseApplication)
    {
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        try
        {
            if (!chooseApplication)
            {
                try
                {
                    using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    return Task.FromResult(true);
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNoAssociation)
                {
                    // Pas d'application associée : Windows propose d'en choisir une.
                }
            }

            // Fenêtre « Ouvrir avec » de Windows (le chemin est passé tel quel, sans guillemets, comme l'attend shell32).
            using var picker = Process.Start(new ProcessStartInfo("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + path)
            {
                UseShellExecute = false,
            });
            return Task.FromResult(picker is not null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return Task.FromResult(false);
        }
    }
}
