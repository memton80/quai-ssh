using Microsoft.UI.Xaml.Markup;

namespace Quai.App.Localization;

/// <summary>
/// Extension de balisage XAML : <c>{l:Str Key=Nav_Connections}</c> retourne la chaîne localisée.
/// Fonctionne sur n'importe quelle propriété (y compris attachée), contrairement à <c>x:Uid</c>.
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class StrExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() => Loc.Get(Key);
}
