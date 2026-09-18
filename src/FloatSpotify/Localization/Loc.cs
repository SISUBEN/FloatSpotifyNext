using System.ComponentModel;
using System.Globalization;

namespace FloatSpotify.Localization;

public sealed class Loc : INotifyPropertyChanged
{
    internal const string IndexerPropertyName = "Item[]";

    private static AppLanguage _language = AppLanguage.ChineseSimplified;

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

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

            LanguageChanged?.Invoke();
            Instance.PropertyChanged?.Invoke(
                Instance,
                new PropertyChangedEventArgs(IndexerPropertyName));
        }
    }

    public string this[string key] => T(key);

    public static string T(string key) => Strings.Lookup(key, _language) ?? key;

    public static string F(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, T(key), arguments);

    public static AppLanguage Resolve(LanguageChoice choice) => choice switch
    {
        LanguageChoice.English => AppLanguage.English,
        LanguageChoice.ChineseSimplified => AppLanguage.ChineseSimplified,
        _ => DetectSystemLanguage()
    };

    public static LanguageChoice ToChoice(AppLanguage? stored) => stored switch
    {
        AppLanguage.English => LanguageChoice.English,
        AppLanguage.ChineseSimplified => LanguageChoice.ChineseSimplified,
        _ => LanguageChoice.System
    };

    public static AppLanguage? ToStored(LanguageChoice choice) => choice switch
    {
        LanguageChoice.English => AppLanguage.English,
        LanguageChoice.ChineseSimplified => AppLanguage.ChineseSimplified,
        _ => null
    };

    public static AppLanguage DetectSystemLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.ChineseSimplified
            : AppLanguage.English;
}
