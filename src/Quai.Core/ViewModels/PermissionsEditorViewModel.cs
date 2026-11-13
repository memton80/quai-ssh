using CommunityToolkit.Mvvm.ComponentModel;
using Quai.Core.Sftp;

namespace Quai.Core.ViewModels;

/// <summary>Éditeur de droits POSIX : neuf cases à cocher synchronisées avec la notation octale.</summary>
public sealed partial class PermissionsEditorViewModel : ObservableObject
{
    private bool _updating;

    public PermissionsEditorViewModel(string name, int mode)
    {
        Name = name;
        Octal = string.Empty;
        Mode = mode & UnixPermissions.Mask;
    }

    public string Name { get; }

    /// <summary>Les neuf bits rwx.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OwnerRead), nameof(OwnerWrite), nameof(OwnerExecute))]
    [NotifyPropertyChangedFor(nameof(GroupRead), nameof(GroupWrite), nameof(GroupExecute))]
    [NotifyPropertyChangedFor(nameof(OthersRead), nameof(OthersWrite), nameof(OthersExecute))]
    [NotifyPropertyChangedFor(nameof(Symbolic))]
    public partial int Mode { get; set; }

    /// <summary>Notation octale saisissable (« 755 ») ; une saisie invalide laisse le mode inchangé.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string Octal { get; set; }

    public bool IsValid => UnixPermissions.TryParseOctal(Octal, out _);

    public string Symbolic => UnixPermissions.ToSymbolic(Mode)[1..];

    public bool OwnerRead { get => Has(UnixPermissions.OwnerRead); set => Set(UnixPermissions.OwnerRead, value); }

    public bool OwnerWrite { get => Has(UnixPermissions.OwnerWrite); set => Set(UnixPermissions.OwnerWrite, value); }

    public bool OwnerExecute { get => Has(UnixPermissions.OwnerExecute); set => Set(UnixPermissions.OwnerExecute, value); }

    public bool GroupRead { get => Has(UnixPermissions.GroupRead); set => Set(UnixPermissions.GroupRead, value); }

    public bool GroupWrite { get => Has(UnixPermissions.GroupWrite); set => Set(UnixPermissions.GroupWrite, value); }

    public bool GroupExecute { get => Has(UnixPermissions.GroupExecute); set => Set(UnixPermissions.GroupExecute, value); }

    public bool OthersRead { get => Has(UnixPermissions.OthersRead); set => Set(UnixPermissions.OthersRead, value); }

    public bool OthersWrite { get => Has(UnixPermissions.OthersWrite); set => Set(UnixPermissions.OthersWrite, value); }

    public bool OthersExecute { get => Has(UnixPermissions.OthersExecute); set => Set(UnixPermissions.OthersExecute, value); }

    partial void OnModeChanged(int value)
    {
        if (!_updating)
        {
            _updating = true;
            Octal = UnixPermissions.ToOctal(value);
            _updating = false;
        }
    }

    partial void OnOctalChanged(string value)
    {
        if (!_updating && UnixPermissions.TryParseOctal(value, out var mode))
        {
            _updating = true;
            Mode = mode;
            _updating = false;
        }
    }

    private bool Has(int bit) => (Mode & bit) != 0;

    private void Set(int bit, bool value) => Mode = value ? Mode | bit : Mode & ~bit;
}
