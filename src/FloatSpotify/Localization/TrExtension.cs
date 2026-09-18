using System.Windows;
using System.Windows.Markup;

namespace FloatSpotify.Localization;

[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is not IProvideValueTarget target ||
            target.TargetProperty is not DependencyProperty)
        {
            return Loc.T(Key);
        }

        var binding = new System.Windows.Data.Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = System.Windows.Data.BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
