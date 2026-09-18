using System.Net.Http;
using System.Net.Http.Headers;

namespace FloatSpotify.Playback;

public sealed class LyricsCoordinator
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan NetEaseTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan KugouTimeout = TimeSpan.FromSeconds(6);

    private static readonly TimeSpan MissLifetime = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan ErrorLifetime = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly IReadOnlyDictionary<LyricsSource, ILyricsProvider> _providers;

    private ILyricsProvider[] _activeChain = Array.Empty<ILyricsProvider>();
    private int _revision;
    private readonly Dictionary<LyricsSource, DateTimeOffset> _cooldowns = new();
    private readonly Dictionary<(LyricsSource, string, string, long), DateTimeOffset> _misses = new();

    public LyricsCoordinator()
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FloatSpotify", "2.0"));

        _providers = new ILyricsProvider[]
        {
            new LrclibLyricsProvider(httpClient),
            new WordLyricsProvider(httpClient, LyricsSource.Karalyr),
            new WordLyricsProvider(httpClient, LyricsSource.BetterLyrics),
            new NetEaseLyricsProvider(httpClient),
            new KugouLyricsProvider(httpClient)
        }.ToDictionary(provider => provider.Source);
    }

    internal LyricsCoordinator(IEnumerable<ILyricsProvider> providers) =>
        _providers = providers.ToDictionary(p => p.Source);

    public int Revision
    {
        get
        {
            lock (_gate)
                return _revision;
        }
    }

    public void ApplyConfiguration(IReadOnlyList<LyricsSource> orderedEnabledSources)
    {
        lock (_gate)
        {
            var chain = orderedEnabledSources
                .Where(_providers.ContainsKey)
                .Distinct()
                .Select(source => _providers[source])
                .ToArray();

            if (chain.SequenceEqual(_activeChain))
                return;

            _activeChain = chain;
            _revision++;
        }
    }

    public async Task<IReadOnlyList<TimedLyric>> GetAsync(
        string track,
        string artist,
        TimeSpan duration,
        CancellationToken cancellationToken,
        Action<IReadOnlyList<TimedLyric>>? onAvailable = null)
    {
        ILyricsProvider[] chain;
        lock (_gate)
            chain = _activeChain;

        if (chain.Length == 0)
            return Array.Empty<TimedLyric>();

        cancellationToken.ThrowIfCancellationRequested();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var state = new RaceState();
        await RaceAsync(chain, track, artist, duration, cancellationToken, onAvailable, state);
        watch.Stop();

        LyricsDiagnostics.Write(new LyricsFetchReport(
            track,
            artist,
            duration.TotalSeconds,
            state.BestSource,
            state.BestQuality,
            state.BestMilliseconds,
            watch.ElapsedMilliseconds,
            state.Attempts));

        return state.Best ?? Array.Empty<TimedLyric>();
    }

    private async Task RaceAsync(
        IEnumerable<ILyricsProvider> providers,
        string track,
        string artist,
        TimeSpan duration,
        CancellationToken cancellationToken,
        Action<IReadOnlyList<TimedLyric>>? onAvailable,
        RaceState state)
    {
        var remaining = new List<Racer>();
        foreach (var provider in providers)
        {
            if (TryGetSuppressionReason(provider.Source, track, artist, duration, out var reason))
            {
                state.Attempts.Add(new LyricsAttempt(provider.Source, reason, 0, 0, 0));
                continue;
            }

            remaining.Add(new Racer(
                provider.Source,
                FetchAsync(provider, track, artist, duration, cancellationToken),
                IsEnhancement(provider.Source)));
        }

        while (remaining.Count > 0)
        {
            var finished = await Task.WhenAny(remaining.Select(racer => racer.Task));
            remaining.RemoveAll(racer => racer.Task == finished);

            cancellationToken.ThrowIfCancellationRequested();

            var result = await finished;
            state.Attempts.Add(result.Attempt);

            var lyrics = result.Lyrics;
            if (lyrics.Count == 0)
                continue;

            var quality = Quality(lyrics);
            if (state.Best is not null && quality <= state.BestQuality)
                continue;

            state.Best = lyrics;
            state.BestQuality = quality;
            state.BestSource = result.Attempt.Source;
            state.BestMilliseconds = result.Attempt.Milliseconds;
            onAvailable?.Invoke(state.Best);

            if (quality >= 2)
                break;

            if (!remaining.Any(racer => racer.IsEnhancement))
                break;
        }

        foreach (var racer in remaining)
            state.Attempts.Add(new LyricsAttempt(racer.Source, LyricsOutcome.Abandoned, 0, 0, 0));
    }

    private async Task<FetchResult> FetchAsync(
        ILyricsProvider provider,
        string track,
        string artist,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BudgetFor(provider.Source));

        var watch = System.Diagnostics.Stopwatch.StartNew();

        FetchResult Result(string outcome, IReadOnlyList<TimedLyric>? lyrics = null) =>
            new(
                lyrics ?? Array.Empty<TimedLyric>(),
                new LyricsAttempt(
                    provider.Source,
                    outcome,
                    watch.ElapsedMilliseconds,
                    lyrics?.Count ?? 0,
                    lyrics is null ? 0 : WordLines(lyrics)));

        try
        {
            var lyrics = await provider.GetAsync(track, artist, duration, timeout.Token);
            if (!LyricVersionMatch.TextMatches(track, lyrics))
                return Result(LyricsOutcome.WrongLanguage);

            if (lyrics.Any(line => !string.IsNullOrWhiteSpace(line.Text)))
                return Result(LyricsOutcome.Hit, lyrics);

            RecordMiss(provider.Source, track, artist, duration, MissLifetime);
            return Result(LyricsOutcome.NotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result(LyricsOutcome.Cancelled);
        }
        catch (OperationCanceledException)
        {
            RecordMiss(provider.Source, track, artist, duration, ErrorLifetime);
            System.Diagnostics.Trace.WriteLine($"Lyrics {provider.Source}: timed out; peers keep racing.");
            return Result(LyricsOutcome.Timeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or
            System.Xml.XmlException or FormatException or InvalidOperationException or
            System.IO.IOException or OverflowException)
        {
            var delay = ex is LyricsUnavailableException unavailable
                ? unavailable.RetryAfter
                : TimeSpan.FromSeconds(30);
            lock (_gate)
                _cooldowns[provider.Source] = DateTimeOffset.UtcNow.Add(delay);
            System.Diagnostics.Trace.WriteLine($"Lyrics {provider.Source}: {ex.GetType().Name}; peers keep racing.");
            return Result(LyricsOutcome.Error(ex.GetType().Name));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Lyrics {provider.Source}: unexpected {ex.GetType().Name}.");
            return Result(LyricsOutcome.Error(ex.GetType().Name));
        }
    }

    private bool TryGetSuppressionReason(
        LyricsSource source,
        string track,
        string artist,
        TimeSpan duration,
        out string reason)
    {
        var key = MissKey(source, track, artist, duration);
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_cooldowns.TryGetValue(source, out var until) && until > now)
            {
                reason = LyricsOutcome.Cooling;
                return true;
            }

            if (_misses.TryGetValue(key, out var missedUntil) && missedUntil > now)
            {
                reason = LyricsOutcome.Remembered;
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    private void RecordMiss(
        LyricsSource source,
        string track,
        string artist,
        TimeSpan duration,
        TimeSpan lifetime)
    {
        var key = MissKey(source, track, artist, duration);
        lock (_gate)
        {
            if (_misses.Count >= 512)
                _misses.Clear();
            _misses[key] = DateTimeOffset.UtcNow.Add(lifetime);
        }
    }

    private static (LyricsSource, string, string, long) MissKey(
        LyricsSource source, string track, string artist, TimeSpan duration) =>
        (source, track, artist, (long)Math.Round(duration.TotalSeconds));

    private static bool IsEnhancement(LyricsSource source) =>
        source is LyricsSource.Karalyr or LyricsSource.BetterLyrics or LyricsSource.Kugou;

    private static TimeSpan BudgetFor(LyricsSource source) => source switch
    {
        LyricsSource.NetEase => NetEaseTimeout,
        LyricsSource.Kugou => KugouTimeout,
        _ => ProviderTimeout
    };

    private static int Quality(IReadOnlyList<TimedLyric> lyrics) =>
        lyrics.Any(line => line.Words.Count > 0) ? 2
        : lyrics.Any(line => !line.IsPlainText && !string.IsNullOrWhiteSpace(line.Text)) ? 1 : 0;

    private static int WordLines(IReadOnlyList<TimedLyric> lyrics) =>
        lyrics.Count(line => line.Words.Count > 0);

    private sealed record Racer(
        LyricsSource Source,
        Task<FetchResult> Task,
        bool IsEnhancement);

    private sealed record FetchResult(IReadOnlyList<TimedLyric> Lyrics, LyricsAttempt Attempt);

    private sealed class RaceState
    {
        public IReadOnlyList<TimedLyric>? Best { get; set; }
        public int BestQuality { get; set; }
        public LyricsSource? BestSource { get; set; }
        public long BestMilliseconds { get; set; }

        public List<LyricsAttempt> Attempts { get; } = new();
    }
}
