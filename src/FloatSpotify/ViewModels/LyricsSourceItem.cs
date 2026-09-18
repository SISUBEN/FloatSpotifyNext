using FloatSpotify.Localization;
using FloatSpotify.Playback;

namespace FloatSpotify.ViewModels;

public sealed class LyricsSourceItem : ObservableObject
{
    private readonly Action<LyricsSourceItem> _onChanged;

    private bool _enabled;
    private bool _canMoveUp;
    private bool _canMoveDown;

    public LyricsSourceItem(LyricsSource source, bool enabled, Action<LyricsSourceItem> onChanged)
    {
        Source = source;
        _enabled = enabled;
        _onChanged = onChanged;

        Loc.LanguageChanged += RefreshLocalizedText;
    }

    public LyricsSource Source { get; }

    public string DisplayName => Source switch
    {
        LyricsSource.Lrclib => Loc.T("LyricsSource_Lrclib_Name"),
        LyricsSource.Karalyr => Loc.T("LyricsSource_Karalyr_Name"),
        LyricsSource.BetterLyrics => Loc.T("LyricsSource_BetterLyrics_Name"),
        LyricsSource.NetEase => Loc.T("LyricsSource_NetEase_Name"),
        LyricsSource.Kugou => Loc.T("LyricsSource_Kugou_Name"),
        _ => Source.ToString()
    };

    public bool IsUnofficial => Source is LyricsSource.NetEase or LyricsSource.Kugou;

    public string Note => Loc.T(IsUnofficial ? "LyricsSource_Unofficial" : "LyricsSource_Official");

    public string Description => Source switch
    {
        LyricsSource.Lrclib => Loc.T("LyricsSource_Lrclib_Description"),
        LyricsSource.Karalyr => Loc.T("LyricsSource_Karalyr_Description"),
        LyricsSource.BetterLyrics => Loc.T("LyricsSource_BetterLyrics_Description"),
        LyricsSource.NetEase => Loc.T("LyricsSource_NetEase_Description"),
        LyricsSource.Kugou => Loc.T("LyricsSource_Kugou_Description"),
        _ => string.Empty
    };

    private void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(Description));
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            OnPropertyChanged();
            _onChanged(this);
        }
    }

    public bool CanMoveUp
    {
        get => _canMoveUp;
        internal set
        {
            if (_canMoveUp == value) return;
            _canMoveUp = value;
            OnPropertyChanged();
        }
    }

    public bool CanMoveDown
    {
        get => _canMoveDown;
        internal set
        {
            if (_canMoveDown == value) return;
            _canMoveDown = value;
            OnPropertyChanged();
        }
    }
}
