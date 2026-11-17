using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Quai.Core.ViewModels;

namespace Quai.App.Views;

/// <summary>Snippets : commandes enregistrées, envoyées dans un terminal par <c>Ctrl+Maj+P</c>.</summary>
public sealed partial class SnippetsPage : UserControl
{
    public SnippetsPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<SnippetsViewModel>();
        InitializeComponent();
    }

    public SnippetsViewModel ViewModel { get; }

    private void OnSnippetDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        ((sender as FrameworkElement)?.DataContext as SnippetItemViewModel)?.EditCommand.Execute(null);
}
