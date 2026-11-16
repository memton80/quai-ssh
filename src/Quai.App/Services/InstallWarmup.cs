using System.Runtime.InteropServices;

namespace Quai.App.Services;

/// <summary>
/// <c>Quai.exe --warmup</c>, lancé par l'installateur juste après la copie des fichiers, sans fenêtre : lit chaque
/// exécutable et bibliothèque de l'installation et le projette comme image sans rien exécuter. Les fichiers sont neufs
/// et non signés ; l'antivirus de Windows les analyse à leur premier accès. Sans cela, ces analyses tombaient pendant
/// le premier démarrage (10 s pour afficher la fenêtre, puis « Quai ne répond plus » une minute) ; elles ont lieu
/// désormais pendant l'installation, sous la fenêtre de progression de l'installateur.
/// </summary>
internal static class InstallWarmup
{
    private const string Switch = "--warmup";
    private const uint LoadLibraryAsDatafile = 0x00000002;
    private const uint LoadLibraryAsImageResource = 0x00000020;
    private static readonly string[] Extensions = [".exe", ".dll", ".winmd"];

    public static bool IsRequested() =>
        Environment.GetCommandLineArgs().Any(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));

    /// <summary>Retourne toujours 0 : un fichier illisible ne doit pas faire échouer l'installation.</summary>
    public static int Run()
    {
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
        {
            if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
                {
                    stream.CopyTo(Stream.Null);
                }

                // Projection comme image (comme au chargement) sans exécuter le moindre code de la bibliothèque.
                var module = LoadLibraryExW(file, IntPtr.Zero, LoadLibraryAsDatafile | LoadLibraryAsImageResource);
                if (module != IntPtr.Zero)
                {
                    FreeLibrary(module);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Fichier verrouillé ou protégé : il sera analysé au premier lancement, comme avant.
            }
        }

        return 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);
}
