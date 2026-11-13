using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Quai.Core.Models;
using Quai.Core.Storage;

namespace Quai.App.Services;

/// <summary>Applique le thème choisi dans les réglages (contenu de la fenêtre et boutons de la barre de titre).</summary>
internal sealed class ThemeService
{
    private readonly ISettingsService _settings;
    private Window? _window;
    private FrameworkElement? _root;

    public ThemeService(ISettingsService settings)
    {
        _settings = settings;
    }

    public void Attach(Window window, FrameworkElement root)
    {
        _window = window;
        _root = root;
        Apply();
        _settings.Changed += (_, _) => root.DispatcherQueue.TryEnqueue(Apply);
    }

    private void Apply()
    {
        if (_window is null || _root is null)
        {
            return;
        }

        var theme = _settings.Current.Theme;
        _root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        _window.AppWindow.TitleBar.PreferredTheme = theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }
}
