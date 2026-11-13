using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Quai.App.Localization;
using Quai.Core.Storage;
using Quai.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Quai.App.Views;

/// <summary>
/// Un terminal : xterm.js hébergé dans WebView2 (fichiers locaux <c>Assets/Terminal</c>, aucun accès réseau),
/// relié à la session SSH par des messages JSON.
/// </summary>
public sealed partial class TerminalView : UserControl
{
    // Hôte virtuel servi depuis le dossier local ; le domaine « .example » est réservé et ne résout jamais.
    private const string HostName = "terminal.quai.example";
    private readonly ISettingsService _settings;
    private readonly MemoryStream _pending = new();
    private readonly Lock _pendingGate = new();
    private bool _flushQueued;
    private bool _initializing;
    private bool _ready;
    private bool _closed;

    public TerminalView(TerminalTabViewModel viewModel)
    {
        ViewModel = viewModel;
        _settings = App.Current.Services.GetRequiredService<ISettingsService>();
        InitializeComponent();

        ViewModel.Output += OnOutput;
        _settings.Changed += OnSettingsChanged;
        Loaded += OnLoaded;
        ActualThemeChanged += (_, _) => SendOptions();
    }

    public TerminalTabViewModel ViewModel { get; }

    /// <summary>La page xterm.js est chargée et a annoncé sa taille.</summary>
    public bool IsReady => _ready;

    /// <summary>Donne le focus clavier au terminal.</summary>
    public void FocusTerminal()
    {
        if (_ready && !_closed)
        {
            WebView.Focus(FocusState.Programmatic);
            Post("{\"type\":\"focus\"}");
        }
    }

    /// <summary>Libère le navigateur embarqué (onglet fermé).</summary>
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        ViewModel.Output -= OnOutput;
        _settings.Changed -= OnSettingsChanged;
        try
        {
            WebView.Close();
        }
        catch (Exception)
        {
            // Déjà fermé.
        }
    }

    /// <summary>Capture du contenu du terminal (test de fumée).</summary>
    internal async Task CapturePreviewAsync(string path)
    {
        using var stream = new InMemoryRandomAccessStream();
        await WebView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        stream.Seek(0);
        await using var file = File.Create(path);
        await stream.AsStreamForRead().CopyToAsync(file);
    }

    /// <summary>Texte actuellement affiché par xterm.js (test de fumée).</summary>
    internal async Task<string> ReadScreenTextAsync()
    {
        var json = await WebView.CoreWebView2.ExecuteScriptAsync(
            "(document.querySelector('.xterm-rows') || document.body).textContent");
        return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
    }

    private static string ThemeName(ElementTheme theme) => theme == ElementTheme.Light ? "light" : "dark";

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initializing || _closed)
        {
            return;
        }

        _initializing = true;
        try
        {
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            // Typiquement : WebView2 Runtime absent (Windows 10 sans Edge à jour).
            WebViewError.Message = Loc.Format("Terminal_WebViewErrorMessage", ex.Message);
            WebViewError.IsOpen = true;
        }
    }

    private async Task InitializeAsync()
    {
        WebView.DefaultBackgroundColor = ActualTheme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(255, 0xFA, 0xFA, 0xFA)
            : Windows.UI.Color.FromArgb(255, 0x0C, 0x0C, 0x0C);

        await WebView.EnsureCoreWebView2Async();
        var core = WebView.CoreWebView2;

        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = true;

        // La page ne peut ni naviguer ailleurs ni ouvrir de fenêtre.
        core.NavigationStarting += (_, args) =>
        {
            if (!args.Uri.StartsWith($"https://{HostName}/", StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
            }
        };
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.WebMessageReceived += OnWebMessageReceived;

        core.SetVirtualHostNameToFolderMapping(
            HostName,
            Path.Combine(AppContext.BaseDirectory, "Assets", "Terminal"),
            CoreWebView2HostResourceAccessKind.DenyCors);

        var appearance = _settings.Current;
        var query = $"theme={ThemeName(ActualTheme)}&fontSize={appearance.TerminalFontSize}&fontFamily={Uri.EscapeDataString(appearance.TerminalFontFamily)}";
        WebView.Source = new Uri($"https://{HostName}/terminal.html?{query}");
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var message = document.RootElement;
            switch (message.GetProperty("type").GetString())
            {
                case "ready":
                    _ready = true;
                    FlushPending();
                    _ = ViewModel.ConnectAsync(message.GetProperty("cols").GetInt32(), message.GetProperty("rows").GetInt32());
                    break;
                case "input":
                    ViewModel.SendInput(message.GetProperty("data").GetString());
                    break;
                case "binary":
                    ViewModel.SendBinary(message.GetProperty("data").GetString());
                    break;
                case "resize":
                    ViewModel.Resize(message.GetProperty("cols").GetInt32(), message.GetProperty("rows").GetInt32());
                    break;
                case "copy":
                    Copy(message.GetProperty("text").GetString());
                    break;
                case "paste":
                    _ = PasteAsync();
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Message inattendu de la page : ignoré.
        }
    }

    private static void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private async Task PasteAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                return;
            }

            var text = await content.GetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                Post(JsonSerializer.Serialize(new { type = "paste", text }));
            }
        }
        catch (Exception)
        {
            // Presse-papiers verrouillé par une autre application : on ne colle rien.
        }
    }

    /// <summary>Réglages modifiés (éventuellement depuis un autre fil) : police et taille appliquées à chaud.</summary>
    private void OnSettingsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(SendOptions);

    private void SendOptions()
    {
        if (_ready && !_closed)
        {
            var appearance = _settings.Current;
            Post(JsonSerializer.Serialize(new
            {
                type = "options",
                theme = ThemeName(ActualTheme),
                fontFamily = appearance.TerminalFontFamily,
                fontSize = appearance.TerminalFontSize,
            }));
        }
    }

    /// <summary>Sortie de la session, reçue hors du fil d'interface : mise en tampon puis envoi groupé.</summary>
    private void OnOutput(byte[] data)
    {
        lock (_pendingGate)
        {
            _pending.Write(data, 0, data.Length);
            if (_flushQueued)
            {
                return;
            }

            _flushQueued = true;
        }

        if (!DispatcherQueue.TryEnqueue(FlushPending))
        {
            lock (_pendingGate)
            {
                _flushQueued = false;
            }
        }
    }

    private void FlushPending()
    {
        byte[] data;
        lock (_pendingGate)
        {
            _flushQueued = false;
            if (!_ready || _closed || _pending.Length == 0)
            {
                // Pas encore prêt : le tampon est conservé et vidé à l'annonce « ready ».
                return;
            }

            data = _pending.ToArray();
            _pending.SetLength(0);
        }

        Post($"{{\"type\":\"output\",\"data\":\"{Convert.ToBase64String(data)}\"}}");
    }

    private void Post(string json)
    {
        if (_closed)
        {
            return;
        }

        try
        {
            WebView.CoreWebView2?.PostWebMessageAsJson(json);
        }
        catch (Exception)
        {
            // Navigateur embarqué en cours de fermeture.
        }
    }
}
