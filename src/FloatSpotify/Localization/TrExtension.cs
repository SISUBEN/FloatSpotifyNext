using System.Windows;
using System.Windows.Markup;

namespace FloatSpotify.Localization;

/// <summary>
/// XAML 里取当前语言的文案：
/// <code>Text="{loc:Tr Controls_FontSize}"</code>
/// <para>
/// 关键是它返回一个 <see cref="System.Windows.Data.Binding"/> 而不是字符串 ——
/// 绑定源是 <see cref="Loc.Instance"/> 的索引器，语言一变就整体重新求值，
/// 窗口不用重建、也不用重启程序。
/// </para>
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    /// <summary><see cref="Strings"/> 里的 key。</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // 目标不是依赖属性时（例如 Style/Setter 的 Value）绑定没地方挂，
        // 只能当场给出当前值 —— 这种情况切换语言不会自动刷新，写 XAML 时注意避开。
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
