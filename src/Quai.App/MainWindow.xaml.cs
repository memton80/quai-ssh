using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quai.App.Localization;
using Quai.App.Services;
using Quai.App.Views;
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

    // Les pages sont créées à la demande puis conservées : les sessions ouvertes survivent à la navigation.
    private readonly Dictionary<string, FrameworkElement> _pages = [];

    public MainWindow()
    {
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

        NavView.Loaded += (_, _) =>
        {
            if (NavView.SettingsItem is NavigationViewItem settings)
            {
                settings.Content = Loc.Get("Nav_Settings");
            }
        };

        ResizeForDpi(1240, 800);
        NavigateTo(ConnectionsTag);
    }

    /// <summary>Contenu racine de la fenêtre (thème, capture d'écran des tests de fumée).</summary>
    public FrameworkElement Root => RootGrid;

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
