namespace FloatSpotify.Playback;

public interface IPlaybackEngine
{
    IAsyncEnumerable<PlaybackFrame> WatchAsync(
        CancellationToken cancellationToken);

    Task ExecuteAsync(
        PlayerCommand command,
        double value = 0,
        CancellationToken cancellationToken = default);
}
