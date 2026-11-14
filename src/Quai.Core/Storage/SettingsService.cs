using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Models;

namespace Quai.Core.Storage;

/// <summary>Réglages de l'application, lus une fois puis enregistrés à chaque modification.</summary>
public interface ISettingsService
{
    /// <summary>Déclenché après chaque modification enregistrée.</summary>
    event EventHandler? Changed;

    /// <summary>Copie des réglages en vigueur.</summary>
    AppSettings Current { get; }

    /// <summary>Applique une modification, l'enregistre et notifie si quelque chose a changé.</summary>
    void Update(Action<AppSettings> change);
}

public sealed class JsonSettingsService : ISettingsService
{
    private readonly string _path;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private AppSettings? _settings;

    public JsonSettingsService(AppPaths paths, ILogger<JsonSettingsService>? logger = null)
        : this(paths.SettingsFile, logger)
    {
    }

    public JsonSettingsService(string path, ILogger<JsonSettingsService>? logger = null)
    {
        _path = path;
        _logger = logger ?? NullLogger<JsonSettingsService>.Instance;
    }

    public event EventHandler? Changed;

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return Load().Clone();
            }
        }
    }

    public void Update(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (_gate)
        {
            var current = Load();
            var updated = current.Clone();
            change(updated);
            updated.Normalize();
            if (Same(current, updated))
            {
                return;
            }

            _settings = updated;
            try
            {
                JsonFile.Write(_path, updated);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Le réglage reste appliqué pour cette session même s'il n'a pas pu être enregistré.
                _logger.LogWarning(ex, "Enregistrement des réglages impossible");
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool Same(AppSettings a, AppSettings b) =>
        a.Theme == b.Theme
        && a.TerminalFontSize == b.TerminalFontSize
        && a.CheckForUpdates == b.CheckForUpdates
        && string.Equals(a.TerminalFontFamily, b.TerminalFontFamily, StringComparison.Ordinal);

    private AppSettings Load()
    {
        if (_settings is not null)
        {
            return _settings;
        }

        try
        {
            _settings = JsonFile.Read<AppSettings>(_path) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Réglages illisibles, retour aux valeurs par défaut");
            try
            {
                JsonFile.Quarantine(_path);
            }
            catch (Exception quarantine) when (quarantine is IOException or UnauthorizedAccessException)
            {
                // Le fichier illisible reste en place ; il sera remplacé au prochain enregistrement.
            }

            _settings = new AppSettings();
        }

        _settings.Normalize();
        return _settings;
    }
}
