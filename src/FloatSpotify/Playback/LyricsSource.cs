using System.Text.Json.Serialization;

namespace FloatSpotify.Playback;

/// <summary>
/// 歌词来源。与 <see cref="PlaybackSource"/> 分开 —— 播放源和歌词源是两件独立的事。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LyricsSource
{
    /// <summary>lrclib.net。免费、无需鉴权的社区歌词库，默认启用。</summary>
    Lrclib,

    /// <summary>
    /// 网易云音乐。中文曲库覆盖明显更好，但用的是非公开接口，
    /// 属于「随时可能失效、有 ToS 风险」的一类，因此默认关闭，必须由用户主动开启。
    /// </summary>
    NetEase,

    /// <summary>
    /// 酷狗音乐。返回 KRC 逐字歌词，中文曲库覆盖好。
    /// 和网易云一样走非公开接口，因此同样默认关闭。
    /// </summary>
    Kugou,
    Karalyr,
    BetterLyrics
}
