using System.Text.Json.Serialization;

namespace FloatSpotify.Playback;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlaybackSource
{
    Spotify,
    YouTubeMusic
}
