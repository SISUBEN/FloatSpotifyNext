using System.ComponentModel;
using System.Globalization;

namespace FloatSpotify.Localization;

/// <summary>
/// 取文案的唯一入口。
/// <para>
/// 用静态成员是因为它要能从任何地方调用（引擎、异常、托盘菜单都在 UI 线程之外），
/// 而当前语言全局只有一份；<see cref="Instance"/> 只是为了给 XAML 提供一个
/// 能触发 <see cref="INotifyPropertyChanged"/> 的绑定源。
/// </para>
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>
    /// XAML 里 <c>{Binding [Key]}</c> 命中的就是索引器，WPF 约定的属性名是 <c>Item[]</c>。
    /// 改这个名字会让所有 <c>{loc:Tr}</c> 在切换语言后不再刷新。
    /// </summary>
    internal const string IndexerPropertyName = "Item[]";

    private static AppLanguage _language = AppLanguage.ChineseSimplified;

    private Loc()
    {
    }

    /// <summary>XAML 绑定用的单例，别拿来当普通对象用。</summary>
    public static Loc Instance { get; } = new();

    /// <summary>语言真正变化时触发（设成同一个值不会触发）。</summary>
    public static event Action? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value)
                return;

            _language = value;

            // 先通知代码里的监听者（托盘菜单、需要重新取词的列表项），
            // 再通知 XAML 绑定 —— 一次索引器通知就能让所有 {loc:Tr} 重新取值。
            LanguageChanged?.Invoke();
            Instance.PropertyChanged?.Invoke(
                Instance,
                new PropertyChangedEventArgs(IndexerPropertyName));
        }
    }

    /// <summary>XAML 绑定入口：<c>{loc:Tr Key}</c> 最终就是读这个索引器。</summary>
    public string this[string key] => T(key);

    /// <summary>取当前语言的文案；key 不存在时返回 key 本身，方便一眼看出漏翻。</summary>
    public static string T(string key) => Strings.Lookup(key, _language) ?? key;

    /// <summary>带占位符的文案，占位符用 <c>{0}</c> <c>{1}</c>。</summary>
    public static string F(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, T(key), arguments);

    /// <summary>把设置里的选项解析成实际语言，<see cref="LanguageChoice.System"/> 走系统语言。</summary>
    public static AppLanguage Resolve(LanguageChoice choice) => choice switch
    {
        LanguageChoice.English => AppLanguage.English,
        LanguageChoice.ChineseSimplified => AppLanguage.ChineseSimplified,
        _ => DetectSystemLanguage()
    };

    /// <summary>设置里存的（可为空 = 跟随系统）转成下拉框选项。</summary>
    public static LanguageChoice ToChoice(AppLanguage? stored) => stored switch
    {
        AppLanguage.English => LanguageChoice.English,
        AppLanguage.ChineseSimplified => LanguageChoice.ChineseSimplified,
        _ => LanguageChoice.System
    };

    /// <summary>下拉框选项转成要落盘的值，<see cref="LanguageChoice.System"/> 存 null。</summary>
    public static AppLanguage? ToStored(LanguageChoice choice) => choice switch
    {
        LanguageChoice.English => AppLanguage.English,
        LanguageChoice.ChineseSimplified => AppLanguage.ChineseSimplified,
        _ => null
    };

    /// <summary>
    /// <c>CurrentUICulture</c> 读的是 Windows 的「首选 UI 语言」。本项目只有中英两种，
    /// 所以中文（不分地区）一律走简体，其余一律英文。
    /// </summary>
    public static AppLanguage DetectSystemLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.ChineseSimplified
            : AppLanguage.English;
}
