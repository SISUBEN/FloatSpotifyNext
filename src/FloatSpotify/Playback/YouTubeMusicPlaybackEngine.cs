using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FloatSpotify.Localization;
using Windows.Media.Control;

namespace FloatSpotify.Playback;

public sealed class YouTubeMusicPlaybackEngine : IPlaybackEngine, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly LyricsCoordinator _lyricsCoordinator;
    private readonly object _stateGate = new();

    private Task<GlobalSystemMediaTransportControlsSessionManager>? _managerTask;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private PlaybackSnapshot? _snapshot;
    private IReadOnlyList<TimedLyric> _lyrics = Array.Empty<TimedLyric>();
    private Task<IReadOnlyList<TimedLyric>>? _lyricsTask;
    private string? _lyricsTrackId;
    private int _lyricsRevision;
    private long _lyricsRequestId;
    private DateTimeOffset _nextLyricsRetryAt;
    private DateTimeOffset _nextPollAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextManagerAttemptAt = DateTimeOffset.MinValue;
    private string? _managerError;
    private string? _statusMessage;
    private DateTimeOffset _statusMessageUntil;
    private double _lyricOffsetSeconds;
    private bool _disposed;

    public YouTubeMusicPlaybackEngine(LyricsCoordinator lyricsCoordinator)
    {
        _lyricsCoordinator = lyricsCoordinator;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FloatSpotify", "2.0"));
    }

    public async IAsyncEnumerable<PlaybackFrame> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(150));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            EnsureManagerStarted();
            await ObserveManagerAsync(cancellationToken);

            GlobalSystemMediaTransportControlsSessionManager? manager;
            lock (_stateGate)
                manager = _manager;

            if (manager is not null && DateTimeOffset.UtcNow >= _nextPollAt)
            {
                _nextPollAt = DateTimeOffset.UtcNow.AddMilliseconds(750);
                await PollPlaybackAsync(manager, cancellationToken);
            }

            ObserveLyricsTask();
            yield return CreateFrame();
        }
    }

    public async Task ExecuteAsync(
        PlayerCommand command,
        double value = 0,
        CancellationToken cancellationToken = default)
    {
        if (command == PlayerCommand.SetLyricOffset)
        {
            lock (_stateGate)
                _lyricOffsetSeconds = value;
            return;
        }

        if (command == PlayerCommand.Reauthorize)
            return;

        var session = await GetSessionForCommandAsync(cancellationToken);
        if (session is null)
        {
            SetStatusMessage(Loc.T("Ytm_Status_NoSession"), TimeSpan.FromSeconds(5));
            return;
        }

        try
        {
            var succeeded = command switch
            {
                PlayerCommand.TogglePlayback => await session.TryTogglePlayPauseAsync(),
                PlayerCommand.PreviousTrack => await session.TrySkipPreviousAsync(),
                PlayerCommand.NextTrack => await session.TrySkipNextAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
            };

            if (!succeeded)
                SetStatusMessage(Loc.T("Ytm_Status_CommandRejected"), TimeSpan.FromSeconds(5));

            _nextPollAt = DateTimeOffset.MinValue;
        }
        catch (COMException)
        {
            SetStatusMessage(Loc.T("Ytm_Status_SessionExpired"), TimeSpan.FromSeconds(5));
            lock (_stateGate)
                _session = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _httpClient.Dispose();
    }

    private void EnsureManagerStarted()
    {
        lock (_stateGate)
        {
            if (_manager is not null ||
                _managerTask is not null ||
                DateTimeOffset.UtcNow < _nextManagerAttemptAt)
            {
                return;
            }

            _managerTask = RequestManagerAsync();
        }
    }

    private static async Task<GlobalSystemMediaTransportControlsSessionManager> RequestManagerAsync()
    {
        return await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    }

    private async Task ObserveManagerAsync(CancellationToken cancellationToken)
    {
        Task<GlobalSystemMediaTransportControlsSessionManager>? task;
        lock (_stateGate)
            task = _managerTask;

        if (task is null || !task.IsCompleted)
            return;

        try
        {
            var manager = await task.WaitAsync(cancellationToken);
            lock (_stateGate)
            {
                _manager = manager;
                _managerTask = null;
                _managerError = null;
            }
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
        {
            lock (_stateGate)
            {
                _managerTask = null;
                _managerError = "Ytm_ManagerError";
                _nextManagerAttemptAt = DateTimeOffset.UtcNow.AddSeconds(5);
            }
        }
    }

    private async Task PollPlaybackAsync(
        GlobalSystemMediaTransportControlsSessionManager manager,
        CancellationToken cancellationToken)
    {
        try
        {
            var candidate = await FindBestSessionAsync(manager, cancellationToken);
            if (candidate is null)
            {
                lock (_stateGate)
                {
                    _session = null;
                    _snapshot = null;
                }
                return;
            }

            var (session, properties, playbackInfo, _) = candidate.Value;
            var timeline = session.GetTimelineProperties();
            var duration = timeline.EndTime > timeline.StartTime
                ? timeline.EndTime - timeline.StartTime
                : TimeSpan.Zero;
            var track = properties.Title.Trim();
            var artist = FirstNonEmpty(
                properties.Artist,
                properties.AlbumArtist,
                properties.Subtitle,
                "YouTube Music");
            var isPlaying = playbackInfo.PlaybackStatus ==
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var playbackRate = playbackInfo.PlaybackRate ?? 1;
            if (!double.IsFinite(playbackRate) || playbackRate < 0)
                playbackRate = 1;
            var sampledAt = Stopwatch.GetTimestamp();
            var position = TimelinePosition(
                timeline.Position, timeline.StartTime, duration,
                timeline.LastUpdatedTime, DateTimeOffset.UtcNow, isPlaying, playbackRate);
            var id = string.Join(
                "\n",
                session.SourceAppUserModelId,
                artist,
                track,
                duration.Ticks);
            var snapshot = new PlaybackSnapshot(
                id,
                track,
                artist,
                duration,
                position,
                isPlaying,
                sampledAt,
                playbackRate);

            var lyricsRevision = _lyricsCoordinator.Revision;

            lock (_stateGate)
            {
                var trackChanged = _snapshot?.Id != snapshot.Id ||
                                   _lyricsRevision != lyricsRevision;
                _session = session;
                _snapshot = snapshot;

                if (trackChanged || (_lyricsTask is null && _lyrics.Count == 0 && DateTimeOffset.UtcNow >= _nextLyricsRetryAt))
                {
                    _lyrics = Array.Empty<TimedLyric>();
                    _lyricsTrackId = snapshot.Id;
                    _lyricsRevision = lyricsRevision;
                    var requestId = ++_lyricsRequestId;
                    _nextLyricsRetryAt = DateTimeOffset.UtcNow.AddSeconds(30);
                    _lyricsTask = _lyricsCoordinator.GetAsync(
                        snapshot.Track,
                        snapshot.Artist,
                        snapshot.Duration,
                        cancellationToken,
                        available =>
                        {
                            lock (_stateGate)
                            {
                                if (_lyricsRequestId == requestId && _snapshot?.Id == snapshot.Id &&
                                    _lyricsCoordinator.Revision == lyricsRevision)
                                    _lyrics = available;
                            }
                        });
                }
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            SetStatusMessage(Loc.T("Ytm_Status_Unavailable"), TimeSpan.FromSeconds(4));
            lock (_stateGate)
            {
                _session = null;
                _snapshot = null;
            }
        }
    }

    private static async Task<SessionCandidate?> FindBestSessionAsync(
        GlobalSystemMediaTransportControlsSessionManager manager,
        CancellationToken cancellationToken)
    {
        SessionCandidate? best = null;

        foreach (var session in manager.GetSessions())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var source = session.SourceAppUserModelId ?? string.Empty;
            var sourceScore = ScoreSource(source);
            if (sourceScore < 0)
                continue;

            try
            {
                var properties = await session.TryGetMediaPropertiesAsync();
                if (string.IsNullOrWhiteSpace(properties.Title))
                    continue;

                var playbackInfo = session.GetPlaybackInfo();
                var score = sourceScore + (playbackInfo.PlaybackStatus switch
                {
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => 50,
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => 20,
                    _ => 0
                });
                var candidate = new SessionCandidate(session, properties, playbackInfo, score);
                if (best is null || candidate.Score > best.Value.Score)
                    best = candidate;
            }
            catch (COMException)
            {
            }
        }

        return best;
    }

    private async Task<GlobalSystemMediaTransportControlsSession?> GetSessionForCommandAsync(
        CancellationToken cancellationToken)
    {
        GlobalSystemMediaTransportControlsSession? session;
        GlobalSystemMediaTransportControlsSessionManager? manager;
        lock (_stateGate)
        {
            session = _session;
            manager = _manager;
        }

        if (session is not null)
            return session;

        EnsureManagerStarted();
        Task<GlobalSystemMediaTransportControlsSessionManager>? managerTask;
        lock (_stateGate)
            managerTask = _managerTask;

        if (manager is null && managerTask is not null)
        {
            try
            {
                manager = await managerTask.WaitAsync(cancellationToken);
                lock (_stateGate)
                {
                    _manager = manager;
                    _managerTask = null;
                    _managerError = null;
                }
            }
            catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        if (manager is null)
            return null;

        var candidate = await FindBestSessionAsync(manager, cancellationToken);
        if (candidate is null)
            return null;

        lock (_stateGate)
            _session = candidate.Value.Session;
        return candidate.Value.Session;
    }

    private void ObserveLyricsTask()
    {
        Task<IReadOnlyList<TimedLyric>>? task;
        string? trackId;
        lock (_stateGate)
        {
            task = _lyricsTask;
            trackId = _lyricsTrackId;
        }

        if (task is null || !task.IsCompleted)
            return;

        lock (_stateGate)
        {
            if (_snapshot?.Id != trackId || _lyricsTask != task)
                return;

            _lyrics = task.Status == TaskStatus.RanToCompletion
                ? task.Result
                : _lyrics;
            _lyricsTask = null;
        }
    }

    private PlaybackFrame CreateFrame()
    {
        lock (_stateGate)
        {
            var status = DateTimeOffset.UtcNow < _statusMessageUntil
                ? _statusMessage
                : null;

            if (_managerError is not null)
            {
                return new PlaybackFrame(
                    "YouTube Music",
                    Loc.T("Ytm_SessionUnavailable"),
                    Loc.T(_managerError),
                    Loc.T("Ytm_SessionUnavailableHint"),
                    false,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    status);
            }

            if (_manager is null)
            {
                return new PlaybackFrame(
                    "YouTube Music",
                    Loc.T("Ytm_Connecting"),
                    Loc.T("Ytm_ConnectingDetail"),
                    string.Empty,
                    false,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    status);
            }

            if (_snapshot is null)
            {
                return new PlaybackFrame(
                    "YouTube Music",
                    Loc.T("Ytm_Idle"),
                    Loc.T("Ytm_IdleDetail"),
                    Loc.T("Ytm_IdleHint"),
                    false,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    status);
            }

            var position = EstimatedPosition(_snapshot, Stopwatch.GetTimestamp()) + TimeSpan.FromSeconds(_lyricOffsetSeconds);
            var currentIndex = -1;
            for (var index = 0; index < _lyrics.Count; index++)
            {
                if (_lyrics[index].At <= position)
                    currentIndex = index;
                else
                    break;
            }

            string currentLine;
            string nextLine;
            if (_lyricsTask is not null && _lyrics.Count == 0)
            {
                currentLine = $"{_snapshot.Artist} · {_snapshot.Track}";
                nextLine = Loc.T("Lyrics_Matching");
            }
            else if (_lyrics.Count == 0)
            {
                currentLine = $"{_snapshot.Artist} · {_snapshot.Track}";
                nextLine = Loc.T("Lyrics_NotFound_Retrying");
            }
            else
            {
                currentLine = currentIndex >= 0 ? _lyrics[currentIndex].Text : "♪";
                nextLine = currentIndex >= 0 && _lyrics[currentIndex].IsPlainText ? Loc.T("Overlay_UnsyncedScroll") :
                    currentIndex + 1 < _lyrics.Count
                    ? _lyrics[currentIndex + 1].Text
                    : string.Empty;
            }

            return new PlaybackFrame(
                _snapshot.Track,
                _snapshot.Artist,
                currentLine,
                nextLine,
                _snapshot.IsPlaying,
                position,
                _snapshot.Duration,
                status,
                currentIndex >= 0 ? _lyrics[currentIndex] : null);
        }
    }

    private static int ScoreSource(string source)
    {
        if (source.Contains("spotify", StringComparison.OrdinalIgnoreCase))
            return -1;
        if (source.Contains("youtube", StringComparison.OrdinalIgnoreCase))
            return 100;

        string[] browserNames = ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi"];
        return browserNames.Any(name => source.Contains(name, StringComparison.OrdinalIgnoreCase))
            ? 30
            : -1;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }

    private static TimeSpan TimelinePosition(
        TimeSpan position,
        TimeSpan start,
        TimeSpan duration,
        DateTimeOffset lastUpdatedAt,
        DateTimeOffset now,
        bool isPlaying,
        double playbackRate)
    {
        var seconds = (position - start).TotalSeconds;
        // GSMTC Position belongs to LastUpdatedTime, not to this poll. Browsers
        // can reuse a timeline for many polls; resetting its age each time makes
        // lyrics stall/rewind. Rebase once, then use Stopwatch between polls.
        // A missing WinRT timestamp (1601) or a future clock value is not an age.
        if (isPlaying && lastUpdatedAt > DateTimeOffset.UnixEpoch && lastUpdatedAt <= now)
            seconds += (now - lastUpdatedAt).TotalSeconds * playbackRate;

        seconds = Math.Max(0, seconds);
        if (duration > TimeSpan.Zero)
            seconds = Math.Min(seconds, duration.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan EstimatedPosition(PlaybackSnapshot snapshot, long now)
    {
        if (!snapshot.IsPlaying)
            return snapshot.Position;

        var estimated = snapshot.Position +
                        Stopwatch.GetElapsedTime(snapshot.SampledAt, now) * snapshot.PlaybackRate;
        return snapshot.Duration > TimeSpan.Zero && estimated > snapshot.Duration
            ? snapshot.Duration
            : estimated;
    }

    private void SetStatusMessage(string message, TimeSpan duration)
    {
        lock (_stateGate)
        {
            _statusMessage = message;
            _statusMessageUntil = DateTimeOffset.UtcNow.Add(duration);
        }
    }

    private readonly record struct SessionCandidate(
        GlobalSystemMediaTransportControlsSession Session,
        GlobalSystemMediaTransportControlsSessionMediaProperties Properties,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo PlaybackInfo,
        int Score);

    private sealed record PlaybackSnapshot(
        string Id,
        string Track,
        string Artist,
        TimeSpan Duration,
        TimeSpan Position,
        bool IsPlaying,
        long SampledAt,
        double PlaybackRate);
}
