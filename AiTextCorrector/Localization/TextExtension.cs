using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace AiTextCorrector.Localization;

/// <summary>
/// Texte de l'interface en XAML : <c>{loc:Text Main.Clear}</c>. Lié à
/// <see cref="Localizer"/>, il suit les changements de langue sans rouvrir la fenêtre.
/// </summary>
public sealed class TextExtension : MarkupExtension
{
    public TextExtension()
    {
    }

    public TextExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Localizer.Instance, Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
}
