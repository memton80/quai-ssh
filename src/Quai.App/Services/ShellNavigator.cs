using Quai.Core.Services;

namespace Quai.App.Services;

/// <summary>Relais de navigation : la fenêtre principale s'y branche à sa création.</summary>
internal sealed class ShellNavigator : IShellNavigator
{
    public Action<AppPage>? Handler { get; set; }

    public void NavigateTo(AppPage page) => Handler?.Invoke(page);
}
