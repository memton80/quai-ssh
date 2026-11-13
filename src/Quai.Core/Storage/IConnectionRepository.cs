using Quai.Core.Models;

namespace Quai.Core.Storage;

/// <summary>Stockage du carnet de connexions.</summary>
public interface IConnectionRepository
{
    /// <summary>Déclenché après chaque ajout, modification ou suppression.</summary>
    event EventHandler? Changed;

    /// <summary>Copie de toutes les connexions, triées par nom.</summary>
    IReadOnlyList<ConnectionProfile> GetAll();

    ConnectionProfile? Find(Guid id);

    /// <summary>Ajoute la connexion, ou remplace celle qui porte le même identifiant.</summary>
    void Save(ConnectionProfile profile);

    bool Delete(Guid id);
}
