namespace Quai.Core.Services;

public enum AppPage
{
    Connections,
    Terminals,
    Sftp,
    Settings,
}

/// <summary>Navigation entre les pages de la fenêtre principale.</summary>
public interface IShellNavigator
{
    void NavigateTo(AppPage page);
}
