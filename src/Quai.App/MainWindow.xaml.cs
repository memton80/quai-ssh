using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quai.App.Localization;
using Quai.App.Services;
using Quai.App.Terminal;
using Quai.App.Views;
using Quai.Core.Services;
using Quai.Core.ViewModels;
using Windows.Foundation;
using Windows.Graphics;

namespace Quai.App;

/// <summary>Fenêtre principale : barre de titre, navigation latérale et hôte des pages.</summary>
public sealed partial class MainWindow : Window
{
    public const string ConnectionsTag = "connections";
    public const string TerminalsTag = "terminals";
    public const string SftpTag = "sftp";
    public const string SettingsTag = "settings";

    /// <summary>Toutes les pages, dans l'ordre de la navigation.</summary>
    public static readonly IReadOnlyList<string> PageTags = [ConnectionsTag, TerminalsTag, SftpTag, SettingsTag];

    // Délai maximal pour déconnecter proprement les sessions à la fermeture.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    // Les pages sont créées à la demande puis conservées : les sessions ouvertes survivent à la navigation.
    private readonly Dictionary<string, FrameworkElement> _pages = [];

    private readonly ILogger<MainWindow> _logger;

    // Volet de navigation déplié par-dessus le contenu (mode compact ou minimal).
    private bool _paneOverlay;

    // Fermeture : confirmation en cours, puis fermeture accordée (les sessions sont déjà déconnectées).
    private bool _closeRequested;
    private bool _closeAllowed;

    public MainWindow()
    {
        Updates = App.Current.Services.GetRequiredService<UpdateBannerViewModel>();
        _logger = App.Current.Services.GetRequiredService<ILogger<MainWindow>>();
        InitializeComponent();

        Title = "Quai";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Quai.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        // Les boîtes de dialogue et sélecteurs de fichiers ont besoin de la fenêtre.
        var dialogs = App.Current.Services.GetRequiredService<DialogService>();
        dialogs.Root = RootGrid;
        dialogs.WindowId = AppWindow.Id;

        App.Current.Services.GetRequiredService<ShellNavigator>().Handler = page => NavigateTo(page switch
        {
            AppPage.Terminals => TerminalsTag,
            AppPage.Sftp => SftpTag,
            AppPage.Settings => SettingsTag,
            _ => ConnectionsTag,
        });

        App.Current.Services.GetRequiredService<ThemeService>().Attach(this, RootGrid);

        NavView.Loaded += (_, _) =>
        {
            if (NavView.SettingsItem is NavigationViewItem settings)
            {
                settings.Content = Loc.Get("Nav_Settings");
            }
        };

        // Le volet déplié en superposition doit passer devant les terminaux (fenêtres natives).
        TerminalAirspace.OverlayProvider = PaneOverlay;
        NavView.PaneOpening += (_, _) => SetPaneOverlay(NavView.DisplayMode != NavigationViewDisplayMode.Expanded);
        NavView.PaneClosed += (_, _) => SetPaneOverlay(false);
        NavView.DisplayModeChanged += (_, _) =>
            SetPaneOverlay(NavView.IsPaneOpen && NavView.DisplayMode != NavigationViewDisplayMode.Expanded);

        AppWindow.Closing += OnClosing;

        ResizeForDpi(1240, 800);
        NavigateTo(ConnectionsTag);
    }

    /// <summary>Bandeau « nouvelle version disponible ».</summary>
    public UpdateBannerViewModel Updates { get; }

    /// <summary>
    /// Test de fumée : volet posé à côté du contenu (comme en plein écran), replié puis redéplié ; le mode d'affichage
    /// d'origine est rétabli par <see cref="RestorePaneForTest"/>.
    /// </summary>
    internal async Task FoldPaneBesideContentForTestAsync()
    {
        NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        NavView.IsPaneOpen = true;
        await Task.Delay(800);
        NavView.IsPaneOpen = false;
        await Task.Delay(800);
        NavView.IsPaneOpen = true;
        await Task.Delay(1000);
    }

    internal void RestorePaneForTest()
    {
        NavView.IsPaneOpen = false;
        NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.Auto;
    }

    /// <summary>Contenu racine de la fenêtre (thème, capture d'écran des tests de fumée).</summary>
    public FrameworkElement Root => RootGrid;

    /// <summary>Page déjà créée pour cette étiquette, ou <c>null</c> (test de fumée).</summary>
    internal FrameworkElement? FindPage(string tag) => _pages.GetValueOrDefault(tag);

    public void NavigateTo(string tag)
    {
        if (tag == SettingsTag)
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return;
        }

        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag as string == tag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.IsSettingsSelected
            ? SettingsTag
            : (args.SelectedItem as NavigationViewItem)?.Tag as string;

        if (tag is not null)
        {
            ShowPage(tag);
        }
    }

    private void ShowPage(string tag)
    {
        if (!_pages.TryGetValue(tag, out var page))
        {
            page = CreatePage(tag);
            _pages[tag] = page;
            PageHost.Children.Add(page);
        }

        foreach (var (key, element) in _pages)
        {
            element.Visibility = key == tag ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Fermeture de la fenêtre (bouton, Alt+F4) : s'il reste des sessions ouvertes ou des transferts,
    /// l'utilisateur confirme ; puis chaque session est déconnectée proprement avant de quitter.
    /// </summary>
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeAllowed)
        {
            return;
        }

        var services = App.Current.Services;
        var shutdown = services.GetRequiredService<AppShutdown>();
        var open = shutdown.Inventory();
        if (open.IsEmpty)
        {
            // Rien de connecté : les onglets restants (déconnectés) n'ont rien à fermer.
            return;
        }

        args.Cancel = true;
        if (_closeRequested)
        {
            return;
        }

        _closeRequested = true;
        try
        {
            var confirmed = await services.GetRequiredService<DialogService>().ConfirmAsync(
                Loc.Get("Shutdown_Title"),
                shutdown.Describe(open),
                Loc.Get("Shutdown_Confirm"),
                isDestructive: open.ActiveTransfers > 0);
            if (!confirmed)
            {
                return;
            }

            if (!await shutdown.CloseAllAsync(ShutdownTimeout))
            {
                _logger.LogWarning("Des sessions ne se sont pas fermées dans le délai ; fermeture forcée");
            }
        }
        catch (Exception ex)
        {
            // Quitter reste possible même si la fermeture propre échoue.
            _logger.LogError(ex, "Fermeture propre des sessions impossible");
        }
        finally
        {
            _closeRequested = false;
        }

        _closeAllowed = true;
        Close();
    }

    /// <summary>
    /// Fermeture pour installer une mise à jour : les sessions ont déjà été fermées (après confirmation),
    /// l'installateur attend que Quai se termine puis le relance.
    /// </summary>
    internal void CloseForUpdate()
    {
        _closeAllowed = true;
        Close();
    }

    /// <summary>Bouton du bandeau : mettre à jour (ou ouvrir la page de la release), ou annuler le téléchargement.</summary>
    private async void OnUpdateActionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await Updates.ActAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mise à jour interrompue");
        }
    }

    private void SetPaneOverlay(bool value)
    {
        _paneOverlay = value;
        TerminalAirspace.NotifyChanged();
    }

    private IEnumerable<Rect> PaneOverlay()
    {
        if (_paneOverlay && NavView.ActualHeight > 0)
        {
            yield return NavView.TransformToVisual(null).TransformBounds(new Rect(0, 0, NavView.OpenPaneLength, NavView.ActualHeight));
        }
    }

    private static FrameworkElement CreatePage(string tag) => tag switch
    {
        ConnectionsTag => new ConnectionsPage(),
        TerminalsTag => new TerminalsPage(),
        SftpTag => new SftpPage(),
        SettingsTag => new SettingsPage(),
        _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, "Page inconnue."),
    };

    private void ResizeForDpi(int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0)
        {
            scale = 1;
        }

        AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
    }
}
