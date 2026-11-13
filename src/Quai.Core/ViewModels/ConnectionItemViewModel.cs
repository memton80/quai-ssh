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

    public bool UsesPrivateKey => Profile.Authentication == AuthenticationKind.PrivateKey;

    /// <summary>Glyphe : clé (authentification par clé) ou cadenas (mot de passe).</summary>
    public string AuthenticationGlyph => UsesPrivateKey ? "" : "";

    public IRelayCommand OpenTerminalCommand { get; }

    public IAsyncRelayCommand EditCommand { get; }

    public IRelayCommand DuplicateCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }
}
