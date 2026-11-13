namespace Quai.Core.Services;

/// <summary>Suppression locale réversible : envoie un fichier ou un dossier à la corbeille.</summary>
public interface ILocalRecycler
{
    void Recycle(string path);
}
