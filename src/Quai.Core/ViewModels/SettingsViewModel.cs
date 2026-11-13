using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quai.Core.Models;
using Quai.Core.Storage;

namespace Quai.Core.ViewModels;

/// <summary>Page des réglages : chaque modification est appliquée et enregistrée immédiatement.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private bool _loading;

    public SettingsViewModel(ISettingsService settings, AppPaths paths)
    {
        _settings = settings;
        DataDirectory = paths.Root;
        LogsDirectory = paths.LogsDirectory;
        TerminalFontFamily = string.Empty;
        Load();
    }

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

    public string Version => AppInfo.Version;

    public string DataDirectory { get; }

    public string LogsDirectory { get; }

    /// <summary>Index dans <see cref="AppTheme"/> : 0 = système, 1 = clair, 2 = sombre.</summary>
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial string TerminalFontFamily { get; set; }

    /// <summary>Taille en <see cref="double"/> pour se lier directement à un <c>NumberBox</c>.</summary>
    [ObservableProperty]
    public partial double TerminalFontSize { get; set; }

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

    private void Load()
    {
        _loading = true;
        try
        {
            var current = _settings.Current;
            ThemeIndex = (int)current.Theme;
            TerminalFontFamily = current.TerminalFontFamily;
            TerminalFontSize = current.TerminalFontSize;
        }
        finally
        {
            _loading = false;
        }
    }
}
