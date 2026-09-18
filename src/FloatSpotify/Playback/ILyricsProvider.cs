namespace FloatSpotify.Playback;

internal interface ILyricsProvider
{
    LyricsSource Source { get; }

    Task<IReadOnlyList<TimedLyric>> GetAsync(
        string track,
        string artist,
        TimeSpan duration,
        CancellationToken cancellationToken);
}
