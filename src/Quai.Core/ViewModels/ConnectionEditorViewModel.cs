using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quai.Core.Localization;
using Quai.Core.Models;
using Quai.Core.Serial;
using PortNames = Quai.Core.Models.SerialPortName;

namespace Quai.Core.ViewModels;

/// <summary>Fiche de création / modification d'une connexion (SSH ou port série).</summary>
public sealed partial class ConnectionEditorViewModel : ObservableObject
{
    private readonly IStrings _strings;
    private readonly ConnectionProfile _original;
    private readonly ISerialPortCatalog? _serialPorts;

    public ConnectionEditorViewModel(
        IStrings strings,
        ConnectionProfile? existing = null,
        IEnumerable<string>? knownFolders = null,
        bool hasStoredPassword = false,
        bool hasStoredPassphrase = false,
        ISerialPortCatalog? serialPorts = null)
    {
        _strings = strings;
        _serialPorts = serialPorts;
        IsNew = existing is null;
        _original = existing?.Clone() ?? new ConnectionProfile();
        KnownFolders = [.. knownFolders ?? []];
        HasStoredPassword = hasStoredPassword;
        HasStoredPassphrase = hasStoredPassphrase;

        Name = IsNew ? string.Empty : _original.Name;
        var serial = _original.Serial ?? new SerialSettings();
        SerialPortName = serial.PortName;
        BaudRateText = serial.BaudRate.ToString(CultureInfo.InvariantCulture);
        DataBitsIndex = Math.Clamp(serial.DataBits, SerialSettings.MinDataBits, SerialSettings.MaxDataBits) - SerialSettings.MinDataBits;
        ParityIndex = (int)serial.Parity;
        StopBitsIndex = (int)serial.StopBits;
        FlowControlIndex = (int)serial.FlowControl;
        ProtocolIndex = (int)_original.Protocol;
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

    /// <summary>Index dans <see cref="ConnectionProtocol"/> (0 = SSH, 1 = port série).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSsh))]
    [NotifyPropertyChangedFor(nameof(IsSerial))]
    public partial int ProtocolIndex { get; set; }

    public ConnectionProtocol Protocol =>
        ProtocolIndex == (int)ConnectionProtocol.Serial ? ConnectionProtocol.Serial : ConnectionProtocol.Ssh;

    public bool IsSsh => Protocol == ConnectionProtocol.Ssh;

    public bool IsSerial => Protocol == ConnectionProtocol.Serial;

    /// <summary>Ports détectés sur cet ordinateur (le champ accepte aussi un nom saisi à la main).</summary>
    public ObservableCollection<string> SerialPorts { get; } = [];

    /// <summary>La liste a été lue et ne contient aucun port.</summary>
    [ObservableProperty]
    public partial bool NoSerialPortDetected { get; set; }

    [ObservableProperty]
    public partial string SerialPortName { get; set; }

    /// <summary>Vitesses usuelles, en texte pour correspondre exactement à la saisie de la liste déroulante.</summary>
    public IReadOnlyList<string> BaudRates { get; } =
        SerialSettings.StandardBaudRates.Select(rate => rate.ToString(CultureInfo.InvariantCulture)).ToList();

    /// <summary>Vitesse en bauds, saisie libre (liste des vitesses usuelles proposée).</summary>
    [ObservableProperty]
    public partial string BaudRateText { get; set; }

    /// <summary>0 = 5 bits … 3 = 8 bits.</summary>
    [ObservableProperty]
    public partial int DataBitsIndex { get; set; }

    /// <summary>Index dans <see cref="SerialParity"/>.</summary>
    [ObservableProperty]
    public partial int ParityIndex { get; set; }

    /// <summary>Index dans <see cref="SerialStopBits"/>.</summary>
    [ObservableProperty]
    public partial int StopBitsIndex { get; set; }

    /// <summary>Index dans <see cref="SerialFlowControl"/>.</summary>
    [ObservableProperty]
    public partial int FlowControlIndex { get; set; }

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

    /// <summary>Relit la liste des ports série (adaptateur USB branché après l'ouverture de la fiche).</summary>
    [RelayCommand]
    public void RefreshSerialPorts()
    {
        var current = SerialPortName;
        var detected = _serialPorts?.GetPortNames() ?? [];

        // Mise à jour sans Clear : la liste déroulante garde la saisie en cours.
        foreach (var gone in SerialPorts.Except(detected).ToList())
        {
            SerialPorts.Remove(gone);
        }

        for (var i = 0; i < detected.Count; i++)
        {
            if (i >= SerialPorts.Count || SerialPorts[i] != detected[i])
            {
                SerialPorts.Insert(i, detected[i]);
            }
        }

        NoSerialPortDetected = SerialPorts.Count == 0;

        // Nouvelle connexion : le premier port détecté est proposé d'office.
        SerialPortName = string.IsNullOrWhiteSpace(current) ? SerialPorts.FirstOrDefault() ?? string.Empty : current;
    }

    /// <summary>Vérifie la saisie ; en cas d'erreur, <see cref="ErrorMessage"/> décrit ce qu'il faut corriger.</summary>
    public bool Validate()
    {
        if (IsSerial)
        {
            return ValidateSerial();
        }

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
        profile.Protocol = Protocol;
        profile.Serial = IsSerial
            ? new SerialSettings
            {
                PortName = SerialPortName ?? string.Empty,
                BaudRate = ParseBaudRate() ?? SerialSettings.DefaultBaudRate,
                DataBits = SerialSettings.MinDataBits + Math.Clamp(DataBitsIndex, 0, SerialSettings.MaxDataBits - SerialSettings.MinDataBits),
                Parity = Enum.IsDefined((SerialParity)ParityIndex) ? (SerialParity)ParityIndex : SerialParity.None,
                StopBits = Enum.IsDefined((SerialStopBits)StopBitsIndex) ? (SerialStopBits)StopBitsIndex : SerialStopBits.One,
                FlowControl = Enum.IsDefined((SerialFlowControl)FlowControlIndex) ? (SerialFlowControl)FlowControlIndex : SerialFlowControl.None,
            }
            : null;
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

    partial void OnProtocolIndexChanged(int value)
    {
        // La liste des ports n'est lue qu'au besoin, puis à la demande (bouton « Actualiser »).
        if (IsSerial && SerialPorts.Count == 0)
        {
            RefreshSerialPorts();
        }
    }

    private bool ValidateSerial()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(SerialPortName))
        {
            errors.Add(_strings.Get("Editor_ErrorSerialPortRequired"));
        }
        else if (!PortNames.IsValid(SerialPortName))
        {
            errors.Add(_strings.Get("Editor_ErrorSerialPortInvalid"));
        }

        if (ParseBaudRate() is null)
        {
            errors.Add(_strings.Get("Editor_ErrorBaudRate"));
        }

        ErrorMessage = string.Join(Environment.NewLine, errors);
        return errors.Count == 0;
    }

    /// <summary>Vitesse saisie, espaces de milliers tolérés (« 115 200 ») ; <c>null</c> si invalide.</summary>
    private int? ParseBaudRate()
    {
        var digits = string.Concat((BaudRateText ?? string.Empty).Where(c => !char.IsWhiteSpace(c)));
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var rate)
               && rate is >= 1 and <= SerialSettings.MaxBaudRate
            ? rate
            : null;
    }
}
