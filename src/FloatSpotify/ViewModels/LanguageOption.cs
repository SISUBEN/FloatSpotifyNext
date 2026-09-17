using FloatSpotify.Localization;

namespace FloatSpotify.ViewModels;

/// <summary>
/// 「语言」下拉框的一项。
/// <para>
/// 不能直接把 <c>ComboBoxItem</c> 写在 XAML 里绑 <c>Content</c>：ComboBox 收起时显示的
/// <c>SelectionBoxItem</c> 是选中项 Content 的**快照**，语言切换后不会跟着变，
/// 会出现「下拉里是英文、收起后还写着中文」。走 <c>DisplayMemberPath</c> 就不会有这个问题 ——
/// 那时模板里的绑定是活的。
/// </para>
/// </summary>
public sealed class LanguageOption : ObservableObject
{
    public LanguageOption(LanguageChoice choice)
    {
        Choice = choice;
        Loc.LanguageChanged += RefreshDisplayName;
    }

    public LanguageChoice Choice { get; }

    public string DisplayName => Choice switch
    {
        LanguageChoice.ChineseSimplified => Loc.T("Language_ChineseSimplified"),
        LanguageChoice.English => Loc.T("Language_English"),
        _ => Loc.T("Language_System")
    };

    private void RefreshDisplayName() => OnPropertyChanged(nameof(DisplayName));
}
