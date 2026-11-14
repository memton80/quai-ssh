using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Models;

namespace Quai.Core.Storage;

/// <summary>Carnet de connexions persistant dans <c>connections.json</c>.</summary>
public sealed class JsonConnectionRepository : IConnectionRepository
{
    private readonly string _path;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private List<ConnectionProfile>? _items;

    public JsonConnectionRepository(AppPaths paths, ILogger<JsonConnectionRepository>? logger = null)
        : this(paths.ConnectionsFile, logger)
    {
    }

    public JsonConnectionRepository(string path, ILogger<JsonConnectionRepository>? logger = null)
    {
        _path = path;
        _logger = logger ?? NullLogger<JsonConnectionRepository>.Instance;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ConnectionProfile> GetAll()
    {
        lock (_gate)
        {
            return [.. Load()
                .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.Host, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.Clone())];
        }
    }

    public ConnectionProfile? Find(Guid id)
    {
        lock (_gate)
        {
            return Load().FirstOrDefault(p => p.Id == id)?.Clone();
        }
    }

    public void Save(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var copy = profile.Clone();
        copy.Normalize();

        lock (_gate)
        {
            var items = Load();
            var index = items.FindIndex(p => p.Id == copy.Id);
            if (index >= 0)
            {
                items[index] = copy;
            }
            else
            {
                items.Add(copy);
            }

            Persist(items);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Delete(Guid id)
    {
        lock (_gate)
        {
            var items = Load();
            if (items.RemoveAll(p => p.Id == id) == 0)
            {
                return false;
            }

            Persist(items);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private List<ConnectionProfile> Load()
    {
        if (_items is not null)
        {
            return _items;
        }

        try
        {
            var document = JsonFile.Read(_path, QuaiJsonContext.Default.ConnectionDocument);
            _items = document?.Connections ?? [];
            _items.RemoveAll(p => p is null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var backup = JsonFile.Quarantine(_path);
            _logger.LogWarning(ex, "Carnet de connexions illisible, mis de côté dans {Backup}", backup);
            _items = [];
        }

        return _items;
    }

    private void Persist(List<ConnectionProfile> items) =>
        JsonFile.Write(_path, new ConnectionDocument { Connections = items }, QuaiJsonContext.Default.ConnectionDocument);
}
