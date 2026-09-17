using FloatSpotify.Localization;
using FloatSpotify.Playback;

namespace FloatSpotify.ViewModels;

/// <summary>
/// 设置面板里的一行歌词源。
/// <para>
/// 列表顺序即优先级，所以 <see cref="CanMoveUp"/> / <see cref="CanMoveDown"/>
/// 由 <see cref="OverlayViewModel"/> 在重排后统一刷新，这里只负责暴露状态。
/// </para>
/// <para>
/// 显示名和说明都是语言相关的，所以构造时挂上 <see cref="Loc.LanguageChanged"/>：
/// 语言一变就主动通知 WPF 重新取值，否则这几行会一直停在旧语言。
/// （实例与进程同寿，不必取消订阅。）
/// </para>
/// </summary>
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

    /// <summary>
    /// 非官方接口的源要在界面上明确标出来 —— 这类源随时可能失效，不能让用户以为它和官方一样稳。
    /// </summary>
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
