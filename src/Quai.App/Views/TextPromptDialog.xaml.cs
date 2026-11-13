using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Quai.App.Views;

/// <summary>Saisie d'un texte court (nom de fichier ou de dossier).</summary>
public sealed partial class TextPromptDialog : ContentDialog
{
    public TextPromptDialog(string title, string label, string initialValue, string confirmText)
    {
        InitializeComponent();

        Title = title;
        PrimaryButtonText = confirmText;
        ValueBox.Header = label;
        ValueBox.Text = initialValue;
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(initialValue);

        Opened += (_, _) =>
        {
            ValueBox.Focus(FocusState.Programmatic);

            // Présélectionne le nom sans l'extension, comme l'Explorateur de fichiers.
            var dot = initialValue.LastIndexOf('.');
            ValueBox.Select(0, dot > 0 ? dot : initialValue.Length);
        };
    }

    public string Value => ValueBox.Text;

    private void OnTextChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(ValueBox.Text);
}
