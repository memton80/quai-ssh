namespace Quai.Core.Services;

/// <summary>Exécute une action sur le fil d'interface (les sessions SSH notifient depuis d'autres fils).</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
