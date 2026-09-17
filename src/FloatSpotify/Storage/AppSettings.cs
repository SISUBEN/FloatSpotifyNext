using FloatSpotify.Localization;
using FloatSpotify.Playback;
using System.Text.Json.Serialization;

namespace FloatSpotify.Storage;

public sealed class AppSettings
{
    public PlaybackSource PlaybackSource { get; set; } = PlaybackSource.Spotify;

    /// <summary>
    /// 界面语言。<c>null</c>（默认）= 跟随操作系统首选 UI 语言；
    /// 用户在下拉框里明确选过之后才会写进 settings.json。
    /// </summary>
    public AppLanguage? Language { get; set; }

    [JsonIgnore]
    public string SpotifyClientId { get; set; } = string.Empty;
    public double FontSize { get; set; } = 46;
    public string FontFamilyName { get; set; } = "Segoe UI Variable Display";
    public double OverlayWidth { get; set; } = 800;
    public OverlayPlacement OverlayPlacement { get; set; } = OverlayPlacement.Custom;
    public double Opacity { get; set; } = 0.96;
    public string TextColor { get; set; } = "#FFFFFFFF";
    public string SecondaryTextColor { get; set; } = "#B8FFFFFF";
    public bool GlowEnabled { get; set; } = true;
    public bool ShowNextLine { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public bool IsLocked { get; set; }
    public double LyricOffsetSeconds { get; set; }
    public Dictionary<string, double> TrackLyricOffsets { get; set; } = new();
    public double? Left { get; set; }
    public double? Top { get; set; }

    /// <summary>
    /// 歌词源设置。**列表顺序即优先级**（越靠前越先用），只尝试 Enabled 为 true 的源。
    /// </summary>
    public List<LyricsSourceOption> LyricsSources { get; set; } = DefaultLyricsSources();

    /// <summary>
    /// 出厂默认：Karalyr / Better Lyrics 逐字增强和 LRCLIB 行级兜底启用，网易云与酷狗关闭。
    /// 后两者走的都是非公开接口，必须由用户主动开启，所以默认不勾。
    /// </summary>
    public static List<LyricsSourceOption> DefaultLyricsSources() =>
    [
        new LyricsSourceOption { Source = LyricsSource.Lrclib, Enabled = true },
        new LyricsSourceOption { Source = LyricsSource.NetEase, Enabled = false },
        new LyricsSourceOption { Source = LyricsSource.Kugou, Enabled = false },
        new LyricsSourceOption { Source = LyricsSource.Karalyr, Enabled = true },
        new LyricsSourceOption { Source = LyricsSource.BetterLyrics, Enabled = true }
    ];
}

/// <summary>
/// 单个歌词源的设置项。
/// </summary>
public sealed class LyricsSourceOption
{
    public LyricsSource Source { get; set; }

    public bool Enabled { get; set; }
}
