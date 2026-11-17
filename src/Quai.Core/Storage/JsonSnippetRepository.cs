using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Models;

namespace Quai.Core.Storage;

/// <summary>Snippets enregistrés.</summary>
public interface ISnippetRepository
{
    /// <summary>Déclenché après chaque ajout, modification ou suppression.</summary>
    event EventHandler? Changed;

    /// <summary>Copie de tous les snippets, triés par nom.</summary>
    IReadOnlyList<Snippet> GetAll();

    /// <summary>Ajoute le snippet, ou remplace celui qui porte le même identifiant.</summary>
    void Save(Snippet snippet);

    bool Delete(Guid id);
}

/// <summary>Snippets persistés dans <c>snippets.json</c> (même format et mêmes garde-fous que le carnet).</summary>
public sealed class JsonSnippetRepository : ISnippetRepository
{
    private readonly string _path;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private List<Snippet>? _items;

    public JsonSnippetRepository(AppPaths paths, ILogger<JsonSnippetRepository>? logger = null)
        : this(paths.SnippetsFile, logger)
    {
    }

    public JsonSnippetRepository(string path, ILogger<JsonSnippetRepository>? logger = null)
    {
        _path = path;
        _logger = logger ?? NullLogger<JsonSnippetRepository>.Instance;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<Snippet> GetAll()
    {
        lock (_gate)
        {
            return [.. Load()
                .OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(s => s.Clone())];
        }
    }

    public void Save(Snippet snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);

        var copy = snippet.Clone();
        copy.Normalize();

        lock (_gate)
        {
            var items = Load();
            var index = items.FindIndex(s => s.Id == copy.Id);
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
            if (items.RemoveAll(s => s.Id == id) == 0)
            {
                return false;
            }

            Persist(items);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private List<Snippet> Load()
    {
        if (_items is not null)
        {
            return _items;
        }

        try
        {
            var document = JsonFile.Read(_path, QuaiJsonContext.Default.SnippetDocument);
            _items = document?.Snippets ?? [];
            _items.RemoveAll(s => s is null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var backup = JsonFile.Quarantine(_path);
            _logger.LogWarning(ex, "Snippets illisibles, mis de côté dans {Backup}", backup);
            _items = [];
        }

        return _items;
    }

    private void Persist(List<Snippet> items) =>
        JsonFile.Write(_path, new SnippetDocument { Snippets = items }, QuaiJsonContext.Default.SnippetDocument);
}
