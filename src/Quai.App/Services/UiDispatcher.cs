using Microsoft.UI.Dispatching;
using Quai.Core.Services;

namespace Quai.App.Services;

/// <summary>Publie des actions sur le fil d'interface (celui sur lequel ce service est créé).</summary>
internal sealed class UiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread()
        ?? throw new InvalidOperationException("UiDispatcher doit être créé sur le fil d'interface.");

    public void Post(Action action)
    {
        if (_queue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _queue.TryEnqueue(() => action());
        }
    }
}
