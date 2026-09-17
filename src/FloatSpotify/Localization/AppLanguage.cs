namespace FloatSpotify.Localization;

/// <summary>
/// 界面语言。**值不能改**：settings.json 里存的是这个数值。
/// </summary>
public enum AppLanguage
{
    ChineseSimplified = 0,
    English = 1
}

/// <summary>
/// 设置面板里「语言」下拉框的选项。
/// <see cref="System"/> 是「跟随操作系统」，落盘时写成 null，所以它必须是 0。
/// </summary>
public enum LanguageChoice
{
    System = 0,
    ChineseSimplified = 1,
    English = 2
}
