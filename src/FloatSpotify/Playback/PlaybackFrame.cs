namespace FloatSpotify.Playback;

public sealed record PlaybackFrame(
    string Track,
    string Artist,
    string CurrentLine,
    string NextLine,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    string? StatusMessage = null,
    TimedLyric? ActiveLyric = null);
