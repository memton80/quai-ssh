using CommunityToolkit.Mvvm.ComponentModel;
using Quai.Core.Localization;
using Quai.Core.Models;

namespace Quai.Core.ViewModels;

/// <summary>Fiche de création / modification d'une connexion.</summary>
public sealed partial class ConnectionEditorViewModel : ObservableObject
{
    private readonly IStrings _strings;
    private readonly ConnectionProfile _original;

    public ConnectionEditorViewModel(
        IStrings strings,
        ConnectionProfile? existing = null,
        IEnumerable<string>? knownFolders = null,
        bool hasStoredPassword = false,
        bool hasStoredPassphrase = false)
    {
        _strings = strings;
        IsNew = existing is null;
        _original = existing?.Clone() ?? new ConnectionProfile();
        KnownFolders = [.. knownFolders ?? []];
        HasStoredPassword = hasStoredPassword;
        HasStoredPassphrase = hasStoredPassphrase;

        Name = IsNew ? string.Empty : _original.Name;
        Host = _original.Host;
        Port = _original.Port;
        Username = _original.Username;
        AuthenticationIndex = (int)_original.Authentication;
        PrivateKeyPath = _original.PrivateKeyPath ?? string.Empty;
        Folder = _original.Folder;
        TagsText = TagList.Format(_original.Tags);
        Notes = _original.Notes;
        Password = string.Empty;
        Passphrase = string.Empty;
        ErrorMessage = string.Empty;

        // Par défaut on mémorise dans le coffre Windows, sauf si l'utilisateur avait choisi de ne pas le faire.
        SavePassword = IsNew || hasStoredPassword || _original.Authentication != AuthenticationKind.Password;
        SavePassphrase = IsNew || hasStoredPassphrase || _original.Authentication != AuthenticationKind.PrivateKey;
    }

    public bool IsNew { get; }

    public Guid Id => _original.Id;

    public string Title => _strings.Get(IsNew ? "Editor_TitleNew" : "Editor_TitleEdit");

    public IReadOnlyList<string> KnownFolders { get; }

    /// <summary>Un mot de passe est déjà dans le coffre : champ vide = le conserver.</summary>
    public bool HasStoredPassword { get; }

    public bool HasStoredPassphrase { get; }

    public string PasswordPlaceholder => _strings.Get(HasStoredPassword ? "Editor_SecretUnchanged" : "Editor_PasswordPlaceholder");

    public string PassphrasePlaceholder => _strings.Get(HasStoredPassphrase ? "Editor_SecretUnchanged" : "Editor_PassphrasePlaceholder");

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Host { get; set; }

    /// <summary>Port en <see cref="double"/> pour se lier directement à un <c>NumberBox</c>.</summary>
    [ObservableProperty]
    public partial double Port { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; }

    /// <summary>Index dans <see cref="AuthenticationKind"/> (0 = mot de passe, 1 = clé privée).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPasswordAuthentication))]
    [NotifyPropertyChangedFor(nameof(IsPrivateKeyAuthentication))]
    public partial int AuthenticationIndex { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    [ObservableProperty]
    public partial bool SavePassword { get; set; }

    [ObservableProperty]
    public partial string PrivateKeyPath { get; set; }

    [ObservableProperty]
    public partial string Passphrase { get; set; }

    [ObservableProperty]
    public partial bool SavePassphrase { get; set; }

    [ObservableProperty]
    public partial string Folder { get; set; }

    [ObservableProperty]
    public partial string TagsText { get; set; }

    [ObservableProperty]
    public partial string Notes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public AuthenticationKind Authentication =>
        AuthenticationIndex == (int)AuthenticationKind.PrivateKey ? AuthenticationKind.PrivateKey : AuthenticationKind.Password;

    public bool IsPasswordAuthentication => Authentication == AuthenticationKind.Password;

    public bool IsPrivateKeyAuthentication => Authentication == AuthenticationKind.PrivateKey;

    /// <summary>Vérifie la saisie ; en cas d'erreur, <see cref="ErrorMessage"/> décrit ce qu'il faut corriger.</summary>
    public bool Validate()
    {
        var errors = new List<string>();
        var host = (Host ?? string.Empty).Trim();

        if (host.Length == 0)
        {
            errors.Add(_strings.Get("Editor_ErrorHostRequired"));
        }
        else if (host.Any(char.IsWhiteSpace) || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            errors.Add(_strings.Get("Editor_ErrorHostInvalid"));
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            errors.Add(_strings.Get("Editor_ErrorUserRequired"));
        }

        if (double.IsNaN(Port) || Port < 1 || Port > 65535 || Port != Math.Floor(Port))
        {
            errors.Add(_strings.Get("Editor_ErrorPortRange"));
        }

        if (IsPrivateKeyAuthentication && string.IsNullOrWhiteSpace(PrivateKeyPath))
        {
            errors.Add(_strings.Get("Editor_ErrorKeyRequired"));
        }

        ErrorMessage = string.Join(Environment.NewLine, errors);
        return errors.Count == 0;
    }

    /// <summary>Construit la connexion à enregistrer (sans aucun secret).</summary>
    public ConnectionProfile BuildProfile()
    {
        var profile = _original.Clone();
        profile.Name = Name ?? string.Empty;
        profile.Host = Host ?? string.Empty;
        profile.Port = double.IsNaN(Port) ? ConnectionProfile.DefaultPort : (int)Math.Clamp(Port, 1, 65535);
        profile.Username = Username ?? string.Empty;
        profile.Authentication = Authentication;
        profile.PrivateKeyPath = IsPrivateKeyAuthentication ? PrivateKeyPath : null;
        profile.Folder = Folder ?? string.Empty;
        profile.Tags = TagList.Parse(TagsText);
        profile.Notes = Notes ?? string.Empty;
        profile.Normalize();
        return profile;
    }
}
