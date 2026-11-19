using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quai.Core.Local;
using Quai.Core.Models;
using Quai.Core.Storage;

namespace Quai.Core.ViewModels;

/// <summary>Page des réglages : chaque modification est appliquée et enregistrée immédiatement.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private bool _loading;

    public SettingsViewModel(ISettingsService settings, AppPaths paths, ILocalShellCatalog? shells = null)
    {
        _settings = settings;
        DataDirectory = paths.Root;
        LogsDirectory = paths.LogsDirectory;
        TerminalFontFamily = string.Empty;
        LocalShells = shells?.GetShells() ?? [];
        LocalShellNames = LocalShells.Select(s => s.Name).ToList();
        Load();
    }

    /// <summary>Shells détectés au démarrage, proposés comme shell du terminal local.</summary>
    public IReadOnlyList<LocalShell> LocalShells { get; }

    /// <summary>Noms des <see cref="LocalShells"/>, pour la liste déroulante.</summary>
    public IReadOnlyList<string> LocalShellNames { get; }

    public bool HasLocalShells => LocalShells.Count > 0;

    /// <summary>Index dans <see cref="LocalShells"/> du shell ouvert par le bouton « Terminal local ».</summary>
    [ObservableProperty]
    public partial int LocalShellIndex { get; set; }

    /// <summary>Polices à chasse fixe courantes, proposées à la saisie (toute police installée est acceptée).</summary>
    public IReadOnlyList<string> SuggestedFonts { get; } =
    [
        "Cascadia Mono",
        "Cascadia Code",
        "Consolas",
        "Courier New",
        "Lucida Console",
        "JetBrains Mono",
        "Fira Code",
        "Source Code Pro",
    ];

    public double MinTerminalFontSize => AppSettings.MinTerminalFontSize;

    public double MaxTerminalFontSize => AppSettings.MaxTerminalFontSize;

    public double MaxKeepAliveSeconds => AppSettings.MaxKeepAliveSeconds;

    public string Version => AppInfo.Version;

    public string DataDirectory { get; }

    public string LogsDirectory { get; }

    /// <summary>Index dans <see cref="AppTheme"/> : 0 = système, 1 = clair, 2 = sombre.</summary>
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial string TerminalFontFamily { get; set; }

    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    /// <summary>Fond du terminal à la couleur de surface de Windows 11 (sinon fond classique de Windows Terminal).</summary>
    [ObservableProperty]
    public partial bool TerminalFluentBackground { get; set; }

    /// <summary>Taille en <see cref="double"/> pour se lier directement à un <c>NumberBox</c>.</summary>
    [ObservableProperty]
    public partial double TerminalFontSize { get; set; }

    /// <summary>Intervalle du keepalive SSH en secondes (0 = désactivé), en <see cref="double"/> pour un <c>NumberBox</c>.</summary>
    [ObservableProperty]
    public partial double KeepAliveSeconds { get; set; }

    /// <summary>Reconnexion automatique d'un terminal SSH après une coupure.</summary>
    [ObservableProperty]
    public partial bool AutoReconnect { get; set; }

    [RelayCommand]
    public void ResetTerminalFont()
    {
        TerminalFontFamily = AppSettings.DefaultTerminalFontFamily;
        TerminalFontSize = AppSettings.DefaultTerminalFontSize;
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (!_loading && Enum.IsDefined((AppTheme)value))
        {
            _settings.Update(s => s.Theme = (AppTheme)value);
        }
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (!_loading)
        {
            _settings.Update(s => s.CheckForUpdates = value);
        }
    }

    partial void OnTerminalFluentBackgroundChanged(bool value)
    {
        if (!_loading)
        {
            _settings.Update(s => s.TerminalFluentBackground = value);
        }
    }

    partial void OnTerminalFontFamilyChanged(string value)
    {
        // Champ vidé en cours de saisie : on attend un nom avant d'enregistrer.
        if (!_loading && !string.IsNullOrWhiteSpace(value))
        {
            _settings.Update(s => s.TerminalFontFamily = value);
        }
    }

    partial void OnTerminalFontSizeChanged(double value)
    {
        // NumberBox vidé : la valeur devient NaN, on garde le réglage précédent.
        if (!_loading && !double.IsNaN(value))
        {
            _settings.Update(s => s.TerminalFontSize = (int)Math.Round(value));
        }
    }

    partial void OnKeepAliveSecondsChanged(double value)
    {
        if (_loading || double.IsNaN(value))
        {
            return;
        }

        _settings.Update(s => s.KeepAliveSeconds = (int)Math.Round(value));

        // Valeur ramenée dans ses bornes (1 à 4 s deviennent 5 s) : le champ affiche ce qui est réellement appliqué.
        var applied = _settings.Current.KeepAliveSeconds;
        if (applied != value)
        {
            _loading = true;
            try
            {
                KeepAliveSeconds = applied;
            }
            finally
            {
                _loading = false;
            }
        }
    }

    partial void OnLocalShellIndexChanged(int value)
    {
        if (!_loading && value >= 0 && value < LocalShells.Count)
        {
            _settings.Update(s => s.LocalShell = LocalShells[value].Id);
        }
    }

    partial void OnAutoReconnectChanged(bool value)
    {
        if (!_loading)
        {
            _settings.Update(s => s.AutoReconnect = value);
        }
    }

    private void Load()
    {
        _loading = true;
        try
        {
            var current = _settings.Current;
            ThemeIndex = (int)current.Theme;
            TerminalFontFamily = current.TerminalFontFamily;
            TerminalFontSize = current.TerminalFontSize;
            TerminalFluentBackground = current.TerminalFluentBackground;
            CheckForUpdates = current.CheckForUpdates;
            KeepAliveSeconds = current.KeepAliveSeconds;
            AutoReconnect = current.AutoReconnect;
            var shell = LocalShellCatalog.Resolve(LocalShells, current.LocalShell);
            LocalShellIndex = shell is null ? -1 : IndexOf(shell);
        }
        finally
        {
            _loading = false;
        }
    }

    private int IndexOf(LocalShell shell)
    {
        for (var i = 0; i < LocalShells.Count; i++)
        {
            if (ReferenceEquals(LocalShells[i], shell))
            {
                return i;
            }
        }

        return -1;
    }
}
