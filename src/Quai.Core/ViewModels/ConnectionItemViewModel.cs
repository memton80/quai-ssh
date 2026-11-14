using CommunityToolkit.Mvvm.Input;
using Quai.Core.Models;

namespace Quai.Core.ViewModels;

/// <summary>Une ligne du carnet de connexions (instantané en lecture seule + commandes).</summary>
public sealed class ConnectionItemViewModel
{
    public ConnectionItemViewModel(ConnectionProfile profile, ConnectionsViewModel owner)
    {
        Profile = profile;
        OpenTerminalCommand = new RelayCommand(() => owner.OpenTerminal(this));
        OpenSftpCommand = new RelayCommand(() => owner.OpenSftp(this));
        EditCommand = new AsyncRelayCommand(() => owner.EditAsync(this));
        DuplicateCommand = new RelayCommand(() => owner.Duplicate(this));
        DeleteCommand = new AsyncRelayCommand(() => owner.DeleteAsync(this));
    }

    public ConnectionProfile Profile { get; }

    public Guid Id => Profile.Id;

    public string Name => Profile.DisplayName;

    public string Endpoint => Profile.Endpoint;

    public string Folder => Profile.Folder;

    public bool HasFolder => Profile.Folder.Length > 0;

    public IReadOnlyList<string> Tags => Profile.Tags;

    public bool HasTags => Profile.Tags.Count > 0;

    public bool IsSerial => Profile.IsSerial;

    /// <summary>L'explorateur SFTP n'a de sens que sur une connexion SSH.</summary>
    public bool SupportsSftp => !Profile.IsSerial;

    public bool UsesPrivateKey => !Profile.IsSerial && Profile.Authentication == AuthenticationKind.PrivateKey;

    /// <summary>Glyphe : prise USB (port série), clé (authentification par clé) ou cadenas (mot de passe).</summary>
    public string Glyph => IsSerial ? "" : UsesPrivateKey ? "" : "";

    public IRelayCommand OpenTerminalCommand { get; }

    public IRelayCommand OpenSftpCommand { get; }

    public IAsyncRelayCommand EditCommand { get; }

    public IRelayCommand DuplicateCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }
}
