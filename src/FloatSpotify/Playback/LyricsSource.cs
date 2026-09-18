using System.Text.Json.Serialization;

namespace FloatSpotify.Playback;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LyricsSource
{
    Lrclib,

    NetEase,

    Kugou,
    Karalyr,
    BetterLyrics
}
