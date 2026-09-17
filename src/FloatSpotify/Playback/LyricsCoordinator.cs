using System.Net.Http;
using System.Net.Http.Headers;

namespace FloatSpotify.Playback;

/// <summary>
/// 可用歌词优先：所有启用的源**并发竞速**，谁先给出可用歌词谁先上屏，随后质量更高的结果再覆盖。
/// <para>
/// 之所以并发而不是逐源串行：串行的时间开销是「各源耗时之和」。第一个源没有这首歌的歌词时，
/// 用户必须先白等它一次完整往返，第二个源才开始发请求，链越长越慢 ——
/// 这正是「明明有歌词却出得很慢」的直接原因。并发之后，首词延迟只取决于最快命中的那个源。
/// </para>
/// <para>
/// 质量规则：逐字(2) &gt; 行级(1) &gt; 纯文本(0)。**行级歌词一到就上屏，逐字结果到了再覆盖**，
/// 也就是「先有歌词显示，再追求更好的效果」。同质量时先到者胜，不覆盖已上屏的内容，
/// 避免歌词在屏幕上跳来跳去。
/// </para>
/// <para>
/// 收手条件：拿到逐字歌词立刻结束（已到上限）；只有拿到行级歌词时，剩下的行级源不可能更好，
/// 只继续等逐字源。
/// </para>
/// <para>
/// 失败隔离：单个源超时只影响它自己，其余源照常竞速，**不会**因此冷却整个源。
/// 只有服务端明确返回 429 / 5xx 时才冷却该源（并尊重它给的 Retry-After）。
/// </para>
/// </summary>
public sealed class LyricsCoordinator
{
    /// <summary>单个源单次请求的预算。超时只记一次短暂 miss，不冷却整个源。</summary>
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 网易云要连做两次请求（搜索 → 取词）且共用一个预算，所以单独放宽。
    /// 否则搜索刚回来就被掐断，这个源实际上永远拿不到歌词。
    /// </summary>
    private static readonly TimeSpan NetEaseTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 酷狗是**三步**（搜索 → 查歌词文件 → 下载解码），比网易云还多一次往返，
    /// 解码那步又是纯 CPU（base64 → 异或 → zlib），同样算在预算里，所以再放宽一点。
    /// </summary>
    private static readonly TimeSpan KugouTimeout = TimeSpan.FromSeconds(6);

    /// <summary>源明确表示「这首歌没有歌词」时的记忆时长。</summary>
    private static readonly TimeSpan MissLifetime = TimeSpan.FromMinutes(5);

    /// <summary>源这次失败（超时 / 网络抖动）时的记忆时长，短一些，让它有机会恢复。</summary>
    private static readonly TimeSpan ErrorLifetime = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly IReadOnlyDictionary<LyricsSource, ILyricsProvider> _providers;

    private ILyricsProvider[] _activeChain = Array.Empty<ILyricsProvider>();
    private int _revision;
    private readonly Dictionary<LyricsSource, DateTimeOffset> _cooldowns = new();
    private readonly Dictionary<(LyricsSource, string, string, long), DateTimeOffset> _misses = new();

    public LyricsCoordinator()
    {
        // 歌词请求单独用一个 HttpClient：职责和播放状态请求不同，超时与 UA 也各自独立。
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

    /// <summary>
    /// 配置版本号。启用源或优先级变化时递增，引擎据此判断需要为当前歌曲重新取词，
    /// 这样用户在设置里一勾选就立刻生效，不必等下一首歌。
    /// </summary>
    public int Revision
    {
        get
        {
            lock (_gate)
                return _revision;
        }
    }

    /// <summary>
    /// 应用新的启用顺序。未知源会被忽略；顺序与当前完全一致时什么都不做，Revision 保持不变。
    /// <para>
    /// 注意：并发竞速之后，这个顺序不再决定「先问谁」——同一波内先返回的源先上屏。
    /// 它仍然决定配置是否变化（从而触发重新取词）。
    /// </para>
    /// </summary>
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

        // 默认不写（LyricsDiagnostics 自己判断环境变量），关闭时这里只是一次读环境变量。
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

    /// <summary>
    /// 把所有没被冷却 / 记忆的源同时发出去，谁先给出更好的结果谁先上屏。
    /// <para>
    /// 行级源和逐字源一起发，而不是「等行级源有结论再发逐字源」：后者省不下任何请求
    /// （只要还没拿到逐字结果，逐字源最终总要跑），唯一效果是让逐字歌词白晚一拍 ——
    /// 行级源全部落空时甚至晚到秒级。
    /// 「先出歌词、再追求效果」由质量规则保证：行级歌词一到就上屏，逐字结果到了再覆盖。
    /// </para>
    /// </summary>
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
            // 没发出去的源也要记账：否则日志里只有「谁跑了」，
            // 看不出「谁根本没跑、为什么」——那正是排查时最想知道的事。
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
            // 同质量时先到者胜：不覆盖已经上屏的内容，避免歌词在屏幕上跳来跳去。
            if (state.Best is not null && quality <= state.BestQuality)
                continue;

            state.Best = lyrics;
            state.BestQuality = quality;
            state.BestSource = result.Attempt.Source;
            state.BestMilliseconds = result.Attempt.Milliseconds;
            onAvailable?.Invoke(state.Best);

            // 逐字歌词已经是上限，没人能再超过它。
            if (quality >= 2)
                break;

            // 已经有行级歌词：剩下的行级源不可能更好，只有逐字源还值得等。
            if (!remaining.Any(racer => racer.IsEnhancement))
                break;
        }

        // 收手时还在跑的源：没人 await 它们，结果也不计入。
        // 记下来才能解释「为什么没采用酷狗」——它可能只是还没跑完，而不是失败了。
        foreach (var racer in remaining)
            state.Attempts.Add(new LyricsAttempt(racer.Source, LyricsOutcome.Abandoned, 0, 0, 0));
    }

    /// <summary>
    /// 跑单个源。**永远不抛异常**：失败只记在内部状态里，由调用方决定怎么处理。
    /// 这一点是并发设计的前提 —— 提前收手时会有任务没人 await，一旦抛异常就成了未观察的异常。
    /// </summary>
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

        // 每个出口都要带一份诊断，写成局部函数免得 6 个 return 各抄一遍。
        // 计时用 watch.Elapsed（不需要 Stop），所以这里取的是「到这个出口为止」的耗时。
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

            // 源明确回答「这首歌没有歌词」。
            RecordMiss(provider.Source, track, artist, duration, MissLifetime);
            return Result(LyricsOutcome.NotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 整条链路被调用方取消，不必记账。
            return Result(LyricsOutcome.Cancelled);
        }
        catch (OperationCanceledException)
        {
            // 单源超时。**不能**当成「源不可用」去冷却整个源 ——
            // 那样一次网络抖动就会把这个源对所有歌禁掉 30 秒，
            // 表现成「刚打开没歌词，过一会儿又自己好了」。
            RecordMiss(provider.Source, track, artist, duration, ErrorLifetime);
            System.Diagnostics.Trace.WriteLine($"Lyrics {provider.Source}: timed out; peers keep racing.");
            return Result(LyricsOutcome.Timeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or
            System.Xml.XmlException or FormatException or InvalidOperationException or
            System.IO.IOException or OverflowException)
        {
            // 服务端明确限流 / 故障时冷却整个源才是对的（并尊重它给的 Retry-After）；
            // 其余（解析失败、连接抖动）沿用原有的源级冷却。
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
            // 兜底，保证「永不抛异常」的契约。提前收手时可能没人 await 这个任务。
            System.Diagnostics.Trace.WriteLine($"Lyrics {provider.Source}: unexpected {ex.GetType().Name}.");
            return Result(LyricsOutcome.Error(ex.GetType().Name));
        }
    }

    /// <summary>
    /// 这个源这次要不要跳过。返回 true 时 <paramref name="reason"/> 给出原因（写进诊断日志）。
    /// </summary>
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

    /// <summary>
    /// 逐字源。只有它们会产出 <see cref="TimedLyric.Words"/>，因此拿到行级歌词后
    /// 只有它们还值得继续等。
    /// </summary>
    private static bool IsEnhancement(LyricsSource source) =>
        source is LyricsSource.Karalyr or LyricsSource.BetterLyrics or LyricsSource.Kugou;

    /// <summary>
    /// 单次取词的预算。两段式、三段式的源要多跑几次往返，共用一个预算时必须放宽，
    /// 否则「搜索成功但取词被掐断」，表现成这个源永远取不到歌词。
    /// </summary>
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

    /// <summary>
    /// 一个在跑的源。必须把「源」和「是不是逐字源」跟任务一起记下来 ——
    /// 任务完成前读不到它的结果，而收手判断需要提前知道。
    /// </summary>
    private sealed record Racer(
        LyricsSource Source,
        Task<FetchResult> Task,
        bool IsEnhancement);

    /// <summary>单个源的取词结果，附带一份诊断。</summary>
    private sealed record FetchResult(IReadOnlyList<TimedLyric> Lyrics, LyricsAttempt Attempt);

    private sealed class RaceState
    {
        public IReadOnlyList<TimedLyric>? Best { get; set; }
        public int BestQuality { get; set; }
        public LyricsSource? BestSource { get; set; }
        public long BestMilliseconds { get; set; }

        /// <summary>
        /// 只在竞速主循环（单条 async 流）里写，所以不需要并发容器。
        /// 顺序＝「决定跳过的源」在前，其余按完成先后 —— 正好能看出竞速过程。
        /// </summary>
        public List<LyricsAttempt> Attempts { get; } = new();
    }
}
