using FloatSpotify.Playback;

namespace FloatSpotify.ViewModels;

/// <summary>
/// 设置面板里的一行歌词源。
/// <para>
/// 列表顺序即优先级，所以 <see cref="CanMoveUp"/> / <see cref="CanMoveDown"/>
/// 由 <see cref="OverlayViewModel"/> 在重排后统一刷新，这里只负责暴露状态。
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
    }

    public LyricsSource Source { get; }

    public string DisplayName => Source switch
    {
        LyricsSource.Lrclib => "LRCLIB",
        LyricsSource.Karalyr => "Karalyr · 逐字",
        LyricsSource.BetterLyrics => "Better Lyrics · 逐字",
        LyricsSource.NetEase => "网易云音乐",
        LyricsSource.Kugou => "酷狗音乐 · 逐字",
        _ => Source.ToString()
    };

    /// <summary>
    /// 非官方接口的源要在界面上明确标出来 —— 这类源随时可能失效，不能让用户以为它和官方一样稳。
    /// </summary>
    public bool IsUnofficial => Source is LyricsSource.NetEase or LyricsSource.Kugou;

    public string Note => IsUnofficial ? "非官方接口" : "公开接口";

    public string Description => Source switch
    {
        LyricsSource.Lrclib => "lrclib.net。免费、无需鉴权的社区歌词库，欧美曲库覆盖好。",
        LyricsSource.Karalyr => "公开 Enhanced LRC 逐字源；未命中、超时或限流时自动回退。",
        LyricsSource.BetterLyrics => "TTML 逐字/音节源；仅使用匿名缓存，需密钥或低置信度时自动回退。",
        LyricsSource.NetEase => "中文曲库覆盖明显更好。用的是非公开接口，可能随时失效，请自行判断是否使用。",
        LyricsSource.Kugou => "中文曲库覆盖好，且返回 KRC 逐字歌词。用的是非公开接口，可能随时失效，请自行判断是否使用。",
        _ => string.Empty
    };

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
