using System.Runtime.CompilerServices;

namespace FloatSpotify.Playback;

public sealed class PlaybackCoordinator : IPlaybackEngine, IDisposable
{
    private readonly object _stateGate = new();
    private readonly IPlaybackEngine _spotify;
    private readonly IPlaybackEngine _youTubeMusic;

    private PlaybackSource _selectedSource;
    private CancellationTokenSource? _activeWatchCancellation;
    private bool _disposed;

    public PlaybackCoordinator(
        IPlaybackEngine spotify,
        IPlaybackEngine youTubeMusic,
        PlaybackSource selectedSource)
    {
        _spotify = spotify;
        _youTubeMusic = youTubeMusic;
        _selectedSource = selectedSource;
    }

    public PlaybackSource SelectedSource
    {
        get
        {
            lock (_stateGate)
                return _selectedSource;
        }
    }

    public void SelectSource(PlaybackSource source)
    {
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_selectedSource == source)
                return;

            _selectedSource = source;
            _activeWatchCancellation?.Cancel();
        }
    }

    public async IAsyncEnumerable<PlaybackFrame> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IPlaybackEngine engine;
            CancellationTokenSource sourceCancellation;
            lock (_stateGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                engine = EngineFor(_selectedSource);
                sourceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeWatchCancellation = sourceCancellation;
            }

            await using var enumerator = engine
                .WatchAsync(sourceCancellation.Token)
                .GetAsyncEnumerator(sourceCancellation.Token);

            try
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync();
                    }
                    catch (OperationCanceledException)
                        when (sourceCancellation.IsCancellationRequested &&
                              !cancellationToken.IsCancellationRequested)
                    {
                        // Selecting another source restarts the stream with its adapter.
                        break;
                    }

                    if (!hasNext)
                        break;

                    yield return enumerator.Current;
                }
            }
            finally
            {
                lock (_stateGate)
                {
                    if (ReferenceEquals(_activeWatchCancellation, sourceCancellation))
                        _activeWatchCancellation = null;
                }

                sourceCancellation.Dispose();
            }
        }
    }

    public Task ExecuteAsync(
        PlayerCommand command,
        double value = 0,
        CancellationToken cancellationToken = default)
    {
        if (command == PlayerCommand.SetLyricOffset)
        {
            return Task.WhenAll(
                _spotify.ExecuteAsync(command, value, cancellationToken),
                _youTubeMusic.ExecuteAsync(command, value, cancellationToken));
        }

        if (command == PlayerCommand.Reauthorize)
            return _spotify.ExecuteAsync(command, value, cancellationToken);

        IPlaybackEngine engine;
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            engine = EngineFor(_selectedSource);
        }

        return engine.ExecuteAsync(command, value, cancellationToken);
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _activeWatchCancellation?.Cancel();
        }

        if (_spotify is IDisposable spotifyDisposable)
            spotifyDisposable.Dispose();
        if (_youTubeMusic is IDisposable youTubeMusicDisposable)
            youTubeMusicDisposable.Dispose();
    }

    private IPlaybackEngine EngineFor(PlaybackSource source) => source switch
    {
        PlaybackSource.Spotify => _spotify,
        PlaybackSource.YouTubeMusic => _youTubeMusic,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };
}
