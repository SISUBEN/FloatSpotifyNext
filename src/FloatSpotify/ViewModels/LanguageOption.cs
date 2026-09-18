using FloatSpotify.Localization;

namespace FloatSpotify.ViewModels;

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
