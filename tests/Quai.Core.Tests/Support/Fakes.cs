using Quai.Core.Localization;
using Quai.Core.Security;
using Quai.Core.Services;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.Support;

/// <summary>Coffre en mémoire pour les tests.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    public Dictionary<string, string> Items { get; } = [];

    public bool Fail { get; set; }

    public string? Get(string key)
    {
        ThrowIfFailing();
        return Items.GetValueOrDefault(key);
    }

    public void Set(string key, string secret)
    {
        ThrowIfFailing();
        Items[key] = secret;
    }

    public void Remove(string key)
    {
        ThrowIfFailing();
        Items.Remove(key);
    }

    private void ThrowIfFailing()
    {
        if (Fail)
        {
            throw new InvalidOperationException("Coffre indisponible.");
        }
    }
}

/// <summary>Chaînes de test : retourne la clé (et les arguments) pour des assertions lisibles.</summary>
public sealed class KeyStrings : IStrings
{
    public string Get(string key) => key;

    public string Format(string key, params object?[] args) => $"{key}({string.Join(",", args)})";
}

/// <summary>Boîtes de dialogue scriptées.</summary>
public sealed class FakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; } = true;

    /// <summary>Appelé à l'ouverture de la fiche : remplit l'éditeur et retourne vrai pour « Enregistrer ».</summary>
    public Func<ConnectionEditorViewModel, bool>? OnEdit { get; set; }

    public List<string> Confirmations { get; } = [];

    public List<string> Errors { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false)
    {
        Confirmations.Add(message);
        return Task.FromResult(ConfirmResult);
    }

    public Task ShowErrorAsync(string title, string message)
    {
        Errors.Add(message);
        return Task.CompletedTask;
    }

    public Task<bool> EditConnectionAsync(ConnectionEditorViewModel editor) =>
        Task.FromResult(OnEdit?.Invoke(editor) ?? false);
}
