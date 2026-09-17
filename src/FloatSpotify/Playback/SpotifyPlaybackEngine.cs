using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FloatSpotify.Localization;

namespace FloatSpotify.Playback;

public sealed class SpotifyPlaybackEngine : IPlaybackEngine, IDisposable
{
    private const string PlayerBase = "https://api.spotify.com/v1/me/player";
    private static readonly TimeSpan PlaybackPollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DefaultRateLimitDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumRateLimitDelay = TimeSpan.FromDays(7);

    private readonly HttpClient _httpClient;
    private readonly SpotifyAuthClient _authClient;
    private readonly LyricsCoordinator _lyricsCoordinator;
    private readonly Func<string?> _clientIdProvider;
    private readonly SpotifyRateLimitStore _rateLimitStore;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly object _stateGate = new();

    private Task<string>? _accessTokenTask;
    private string? _accessToken;
    private PlaybackSnapshot? _snapshot;
    private IReadOnlyList<TimedLyric> _lyrics = Array.Empty<TimedLyric>();
    private Task<IReadOnlyList<TimedLyric>>? _lyricsTask;
    private string? _lyricsTrackId;
    private int _lyricsRevision;
    private long _lyricsRequestId;
    private DateTimeOffset _nextLyricsRetryAt;
    private DateTimeOffset _nextPollAt = DateTimeOffset.MinValue;
    private DateTimeOffset _rateLimitUntil = DateTimeOffset.MinValue;
    private string? _rateLimitClientId;
    private string? _authErrorKey;
    private bool _reauthorizationInProgress;
    private string? _statusMessage;
    private DateTimeOffset _statusMessageUntil;
    private double _lyricOffsetSeconds;
    private bool _disposed;

    public SpotifyPlaybackEngine(Func<string?> clientIdProvider, LyricsCoordinator lyricsCoordinator)
    {
        _clientIdProvider = clientIdProvider;
        _lyricsCoordinator = lyricsCoordinator;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FloatSpotify", "2.0"));

        _authClient = new SpotifyAuthClient(
            clientIdProvider,
            _httpClient,
            new SpotifySessionStore());
        _rateLimitStore = new SpotifyRateLimitStore();
    }

    public async IAsyncEnumerable<PlaybackFrame> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(150));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            SynchronizeRateLimitState();
            EnsureAuthenticationStarted(cancellationToken);
            await ObserveAuthenticationAsync(cancellationToken);

            if (TryBeginPlaybackPoll(out var token))
                await PollPlaybackAsync(token, cancellationToken);

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
        {
            lock (_stateGate)
            {
                _authErrorKey = null;
                _reauthorizationInProgress = true;
                _accessTokenTask = _authClient.ForceAuthorizationAsync(cancellationToken);
            }
            return;
        }

        SynchronizeRateLimitState();
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            if (IsRateLimited())
                return;

            var token = await _authClient.GetAccessTokenAsync(cancellationToken);
            var path = command switch
            {
                PlayerCommand.TogglePlayback when CurrentIsPlaying() => "pause",
                PlayerCommand.TogglePlayback => "play",
                PlayerCommand.PreviousTrack => "previous",
                PlayerCommand.NextTrack => "next",
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };
            var method = command == PlayerCommand.TogglePlayback
                ? HttpMethod.Put
                : HttpMethod.Post;

            using var request = CreateSpotifyRequest(method, $"{PlayerBase}/{path}", token);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authClient.InvalidateAccessToken(token);
                SetStatusMessage(Loc.T("Spotify_Status_TokenExpired"), TimeSpan.FromSeconds(5));
                lock (_stateGate)
                {
                    _accessToken = null;
                    _accessTokenTask = null;
                }
                return;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                SetStatusMessage(Loc.T("Spotify_Status_PremiumRequired"), TimeSpan.FromSeconds(8));
                return;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                ApplyRateLimit(response);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                SetStatusMessage(
                    Loc.F("Spotify_Status_ControlFailed", (int)response.StatusCode),
                    TimeSpan.FromSeconds(6));
                return;
            }

            lock (_stateGate)
            {
                if (_snapshot is not null && command == PlayerCommand.TogglePlayback)
                    _snapshot = _snapshot with { IsPlaying = !_snapshot.IsPlaying, SampledAt = Stopwatch.GetTimestamp() };
                _nextPollAt = DateTimeOffset.MinValue;
            }
        }
        catch (HttpRequestException)
        {
            SetStatusMessage(Loc.T("Spotify_Status_Offline"), TimeSpan.FromSeconds(6));
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _requestGate.Dispose();
        _httpClient.Dispose();
    }

    private void EnsureAuthenticationStarted(CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            _accessTokenTask ??= _authClient.GetAccessTokenAsync(cancellationToken);
        }
    }

    private async Task ObserveAuthenticationAsync(CancellationToken cancellationToken)
    {
        Task<string>? task;
        lock (_stateGate)
            task = _accessTokenTask;

        if (task is null || !task.IsCompleted)
            return;

        try
        {
            var token = await task.WaitAsync(cancellationToken);
            lock (_stateGate)
            {
                _accessToken = token;
                _authErrorKey = null;
                _reauthorizationInProgress = false;
            }
        }
        catch (SpotifyAuthorizationException exception)
        {
            bool wasReauthorization;
            lock (_stateGate)
            {
                wasReauthorization = _reauthorizationInProgress;
                _reauthorizationInProgress = false;
                _accessTokenTask = null;
                if (!wasReauthorization)
                    _authErrorKey = exception.Key;
            }

            if (wasReauthorization)
                SetStatusMessage(Loc.T(exception.Key), TimeSpan.FromSeconds(10));
        }
        catch (HttpRequestException)
        {
            bool wasReauthorization;
            lock (_stateGate)
            {
                wasReauthorization = _reauthorizationInProgress;
                _reauthorizationInProgress = false;
                _accessTokenTask = null;
                if (!wasReauthorization)
                    _authErrorKey = "Spotify_Auth_NetworkError";
            }

            if (wasReauthorization)
            {
                SetStatusMessage(
                    Loc.T("Spotify_Auth_ReauthorizeFailed"),
                    TimeSpan.FromSeconds(10));
            }
        }
    }

    private async Task PollPlaybackAsync(string token, CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            if (IsRateLimited())
                return;

            token = await RefreshAccessTokenAsync(cancellationToken);

            var requestStartedAt = Stopwatch.GetTimestamp();
            using var request = CreateSpotifyRequest(
                HttpMethod.Get,
                $"{PlayerBase}/currently-playing?additional_types=track,episode",
                token);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseReceivedAt = Stopwatch.GetTimestamp();

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authClient.InvalidateAccessToken(token);
                lock (_stateGate)
                {
                    _accessToken = null;
                    _accessTokenTask = null;
                }
                return;
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                lock (_stateGate)
                    _snapshot = null;
                return;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                ApplyRateLimit(response);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                SetStatusMessage(
                    Loc.F("Spotify_Status_StateFailed", (int)response.StatusCode),
                    TimeSpan.FromSeconds(4));
                return;
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty("item", out var item) || item.ValueKind == JsonValueKind.Null)
            {
                lock (_stateGate)
                    _snapshot = null;
                return;
            }

            var id = item.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;
            var track = item.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            var durationMs = item.TryGetProperty("duration_ms", out var durationElement)
                ? durationElement.GetInt64()
                : 0;
            var artist = ReadArtist(item);
            var progressMs = root.TryGetProperty("progress_ms", out var progressElement) &&
                             progressElement.ValueKind == JsonValueKind.Number
                ? progressElement.GetInt64()
                : 0;
            var isPlaying = root.TryGetProperty("is_playing", out var playingElement) &&
                            playingElement.GetBoolean();

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(track))
                return;

            var snapshot = new PlaybackSnapshot(
                id,
                track,
                artist,
                TimeSpan.FromMilliseconds(durationMs),
                TimeSpan.FromMilliseconds(progressMs),
                isPlaying,
                requestStartedAt + (responseReceivedAt - requestStartedAt) / 2);

            var lyricsRevision = _lyricsCoordinator.Revision;

            lock (_stateGate)
            {
                // 除了换歌，歌词源配置变化（用户勾选/调序）也要重新取词。
                var trackChanged = _snapshot?.Id != snapshot.Id ||
                                   _lyricsRevision != lyricsRevision;
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
        catch (HttpRequestException)
        {
            SetStatusMessage(Loc.T("Spotify_Status_NetworkRetry"), TimeSpan.FromSeconds(4));
        }
        catch (SpotifyAuthorizationException exception)
        {
            lock (_stateGate)
            {
                _accessToken = null;
                _accessTokenTask = null;
                _authErrorKey = exception.Key;
            }
        }
        catch (JsonException)
        {
            SetStatusMessage(Loc.T("Spotify_Status_UnknownState"), TimeSpan.FromSeconds(4));
        }
        finally
        {
            _requestGate.Release();
        }
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
            var now = DateTimeOffset.UtcNow;
            var status = now < _rateLimitUntil
                ? FormatRateLimitStatus(_rateLimitUntil, now)
                : now < _statusMessageUntil
                    ? _statusMessage
                    : null;

            if (_authErrorKey is not null)
            {
                return new PlaybackFrame(
                    "Spotify",
                    Loc.T("Spotify_NeedsAuth"),
                    Loc.T(_authErrorKey),
                    Loc.T("Spotify_ReauthorizeHint"),
                    false,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    status);
            }

            if (_accessToken is null)
            {
                return new PlaybackFrame(
                    "Spotify",
                    Loc.T("Spotify_Connecting"),
                    Loc.T("Spotify_ConnectingDetail"),
                    Loc.T("Spotify_FirstRunHint"),
                    false,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    status);
            }

            if (_snapshot is null)
            {
                return new PlaybackFrame(
                    "Spotify",
                    Loc.T("Spotify_Idle"),
                    Loc.T("Spotify_IdleDetail"),
                    Loc.T("Spotify_IdleHint"),
                    false,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    status);
            }

            var position = EstimatedPosition(_snapshot) + TimeSpan.FromSeconds(_lyricOffsetSeconds);
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

    private bool CurrentIsPlaying()
    {
        lock (_stateGate)
            return _snapshot?.IsPlaying == true;
    }

    private static TimeSpan EstimatedPosition(PlaybackSnapshot snapshot)
    {
        if (!snapshot.IsPlaying)
            return snapshot.Position;

        var elapsed = Stopwatch.GetElapsedTime(snapshot.SampledAt);
        return snapshot.Position + elapsed;
    }

    private static string ReadArtist(JsonElement item)
    {
        if (item.TryGetProperty("artists", out var artists) &&
            artists.ValueKind == JsonValueKind.Array &&
            artists.GetArrayLength() > 0 &&
            artists[0].TryGetProperty("name", out var artistName))
        {
            return artistName.GetString() ?? "Spotify";
        }

        if (item.TryGetProperty("show", out var show) &&
            show.TryGetProperty("name", out var showName))
        {
            return showName.GetString() ?? "Spotify";
        }

        return "Spotify";
    }

    private static HttpRequestMessage CreateSpotifyRequest(
        HttpMethod method,
        string uri,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private bool TryBeginPlaybackPoll(out string token)
    {
        lock (_stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_accessToken is null || now < _nextPollAt || now < _rateLimitUntil)
            {
                token = string.Empty;
                return false;
            }

            token = _accessToken;
            _nextPollAt = now.Add(PlaybackPollInterval);
            return true;
        }
    }

    private bool IsRateLimited()
    {
        lock (_stateGate)
            return DateTimeOffset.UtcNow < _rateLimitUntil;
    }

    private void ApplyRateLimit(HttpResponseMessage response)
    {
        var now = DateTimeOffset.UtcNow;
        var delay = ReadRetryAfter(response, now);
        var retryAt = now.Add(delay);

        DateTimeOffset effectiveRetryAt;
        lock (_stateGate)
        {
            if (retryAt > _rateLimitUntil)
                _rateLimitUntil = retryAt;
            if (_nextPollAt < _rateLimitUntil)
                _nextPollAt = _rateLimitUntil;
            effectiveRetryAt = _rateLimitUntil;
        }

        _rateLimitStore.Save(_clientIdProvider()?.Trim(), effectiveRetryAt);
    }

    private void SynchronizeRateLimitState()
    {
        var clientId = _clientIdProvider()?.Trim() ?? string.Empty;
        lock (_stateGate)
        {
            if (string.Equals(_rateLimitClientId, clientId, StringComparison.Ordinal))
                return;
        }

        var storedRetryAt = _rateLimitStore.Load(clientId);
        lock (_stateGate)
        {
            _rateLimitClientId = clientId;
            _rateLimitUntil = storedRetryAt;
            if (_nextPollAt < storedRetryAt)
                _nextPollAt = storedRetryAt;
        }
    }

    private async Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = await _authClient.GetAccessTokenAsync(cancellationToken);
        lock (_stateGate)
        {
            _accessToken = token;
            _accessTokenTask = Task.FromResult(token);
            _authErrorKey = null;
        }

        return token;
    }

    private static TimeSpan ReadRetryAfter(
        HttpResponseMessage response,
        DateTimeOffset now)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta
                    ?? (retryAfter?.Date is { } retryAt ? retryAt - now : DefaultRateLimitDelay);

        if (delay < TimeSpan.FromSeconds(1))
            return TimeSpan.FromSeconds(1);
        return delay > MaximumRateLimitDelay ? MaximumRateLimitDelay : delay;
    }

    private static string FormatRateLimitStatus(
        DateTimeOffset retryAt,
        DateTimeOffset now)
    {
        var remaining = retryAt - now;
        var localRetryAt = retryAt.ToLocalTime();
        var retryTime = localRetryAt.Date == now.ToLocalTime().Date
            ? localRetryAt.ToString("HH:mm", CultureInfo.InvariantCulture)
            : localRetryAt.ToString(Loc.T("Spotify_Retry_DateFormat"), CultureInfo.InvariantCulture);

        if (remaining >= TimeSpan.FromHours(1))
        {
            var totalMinutes = (int)Math.Ceiling(remaining.TotalMinutes);
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            return minutes == 0
                ? Loc.F("Spotify_Retry_Hours", retryTime, hours)
                : Loc.F("Spotify_Retry_HoursMinutes", retryTime, hours, minutes);
        }

        if (remaining >= TimeSpan.FromMinutes(1))
        {
            var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
            return Loc.F("Spotify_Retry_Minutes", retryTime, minutes);
        }

        var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        return Loc.F("Spotify_Retry_Seconds", retryTime, seconds);
    }

    private void SetStatusMessage(string message, TimeSpan duration)
    {
        lock (_stateGate)
        {
            _statusMessage = message;
            _statusMessageUntil = DateTimeOffset.UtcNow.Add(duration);
        }
    }

    private sealed record PlaybackSnapshot(
        string Id,
        string Track,
        string Artist,
        TimeSpan Duration,
        TimeSpan Position,
        bool IsPlaying,
        long SampledAt);
}
