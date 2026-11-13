using System.Globalization;

namespace Quai.Core.Sftp;

/// <summary>Droits POSIX (lecture / écriture / exécution pour propriétaire, groupe, autres).</summary>
public static class UnixPermissions
{
    public const int OwnerRead = 0x100;
    public const int OwnerWrite = 0x080;
    public const int OwnerExecute = 0x040;
    public const int GroupRead = 0x020;
    public const int GroupWrite = 0x010;
    public const int GroupExecute = 0x008;
    public const int OthersRead = 0x004;
    public const int OthersWrite = 0x002;
    public const int OthersExecute = 0x001;

    /// <summary>Les neuf bits rwx (les bits spéciaux setuid / setgid / sticky sont ignorés).</summary>
    public const int Mask = 0x1FF;

    /// <summary>« rwxr-xr-x », précédé de « d » (dossier), « l » (lien) ou « - ».</summary>
    public static string ToSymbolic(int mode, bool isDirectory = false, bool isLink = false)
    {
        Span<char> text = stackalloc char[10];
        text[0] = isLink ? 'l' : isDirectory ? 'd' : '-';
        const string Letters = "rwxrwxrwx";
        for (var i = 0; i < 9; i++)
        {
            text[i + 1] = (mode & (OwnerRead >> i)) != 0 ? Letters[i] : '-';
        }

        return new string(text);
    }

    /// <summary>Notation octale sur trois chiffres : « 755 ».</summary>
    public static string ToOctal(int mode) =>
        Convert.ToString(mode & Mask, 8).PadLeft(3, '0');

    public static bool TryParseOctal(string? text, out int mode)
    {
        mode = 0;
        var value = text?.Trim() ?? string.Empty;
        if (value.Length is < 3 or > 4 || value.Any(c => c is < '0' or > '7'))
        {
            return false;
        }

        // Quatre chiffres : le premier (bits spéciaux) doit être 0, on ne les gère pas.
        if (value.Length == 4)
        {
            if (value[0] != '0')
            {
                return false;
            }

            value = value[1..];
        }

        mode = Convert.ToInt32(value, 8);
        return true;
    }

    /// <summary>Valeur attendue par SSH.NET : les chiffres octaux lus comme un nombre décimal (0o755 → 755).</summary>
    public static short ToSshNetMode(int mode) =>
        short.Parse(ToOctal(mode), CultureInfo.InvariantCulture);
}
