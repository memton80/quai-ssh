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

    /// <summary>Réponses successives aux demandes de texte ; <c>null</c> = annulation.</summary>
    public Queue<string?> TextAnswers { get; } = new();

    /// <summary>Appelé à l'ouverture de l'éditeur de droits ; retourne vrai pour « Appliquer ».</summary>
    public Func<PermissionsEditorViewModel, bool>? OnEditPermissions { get; set; }

    public List<string> Confirmations { get; } = [];

    public List<string> Errors { get; } = [];

    public List<(string Title, string Initial)> TextRequests { get; } = [];

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

    public Task<string?> PromptTextAsync(string title, string label, string initialValue, string confirmText)
    {
        TextRequests.Add((title, initialValue));
        return Task.FromResult(TextAnswers.Count > 0 ? TextAnswers.Dequeue() : null);
    }

    public Task<bool> EditPermissionsAsync(PermissionsEditorViewModel editor) =>
        Task.FromResult(OnEditPermissions?.Invoke(editor) ?? false);
}

/// <summary>Installation scriptée d'une mise à jour : enregistre ce qui serait lancé.</summary>
public sealed class FakeUpdateInstaller : Quai.Core.Updates.IUpdateInstaller
{
    public bool CanInstall { get; set; } = true;

    /// <summary>Réponse à la confirmation (sessions ouvertes) : faux = l'utilisateur garde ses sessions.</summary>
    public bool Accept { get; set; } = true;

    public Exception? Failure { get; set; }

    public List<string> Installed { get; } = [];

    public List<Uri> OpenedPages { get; } = [];

    public Task<bool> InstallAndRestartAsync(string installerPath)
    {
        if (Failure is not null)
        {
            return Task.FromException<bool>(Failure);
        }

        if (Accept)
        {
            Installed.Add(installerPath);
        }

        return Task.FromResult(Accept);
    }

    public void OpenReleasePage(Uri page) => OpenedPages.Add(page);
}

/// <summary>Réglages en mémoire.</summary>
public sealed class InMemorySettingsService : Quai.Core.Storage.ISettingsService
{
    private Quai.Core.Models.AppSettings _settings = new();

    public event EventHandler? Changed;

    public Quai.Core.Models.AppSettings Current => _settings.Clone();

    public void Update(Action<Quai.Core.Models.AppSettings> change)
    {
        var updated = _settings.Clone();
        change(updated);
        updated.Normalize();
        _settings = updated;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
