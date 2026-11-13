using Microsoft.UI.Xaml.Controls;
using Quai.Core.ViewModels;

namespace Quai.App.Views;

/// <summary>Éditeur de droits POSIX d'un fichier distant.</summary>
public sealed partial class PermissionsDialog : ContentDialog
{
    public PermissionsDialog(PermissionsEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public PermissionsEditorViewModel ViewModel { get; }
}
