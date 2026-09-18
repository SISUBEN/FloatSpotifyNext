using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FloatSpotify.Playback;
using FloatSpotify.Windows;
using FloatSpotify.Storage;

internal static class Program
{
    private static int _passed;
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
        _passed++;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--youtube-timeline")
        {
            YouTubeTimelineTests();
            Console.WriteLine($"Passed {_passed} checks.");
            return;
        }
        if (args.FirstOrDefault() == "--window-probe")
        {
            OverlayRegression.Probe();
            return;
        }
        if (args.FirstOrDefault() == "--i18n-probe")
        {
            LocalizationProbe.Probe();
            return;
        }
        if (args.FirstOrDefault() == "--demo")
        {
            new Application().Run(new LyricDemoWindow(args.ElementAtOrDefault(1)));
            return;
        }
        if (args.FirstOrDefault() == "--live-lyrics")
        {
            LiveLyricsCheck().GetAwaiter().GetResult();
            return;
        }
        if (args.FirstOrDefault() == "--live-diagnostics")
        {
            LiveDiagnostics().GetAwaiter().GetResult();
            return;
        }
        ParserTests();
        YouTubeTimelineTests();
        PlaybackAvailabilityTest();
        ProviderTests().GetAwaiter().GetResult();
        DiagnosticsTests().GetAwaiter().GetResult();
        RenderTests(args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "FloatSpotify-lyric-render"));
        Console.WriteLine($"Passed {_passed} checks.");
    }

    private static void YouTubeTimelineTests()
    {
        var type = typeof(YouTubeMusicPlaybackEngine);
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var timeline = type.GetMethod("TimelinePosition", flags)!;
        var epoch = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        TimeSpan Position(double raw, double age, bool playing = true, double rate = 1,
            double start = 0, double duration = 180, DateTimeOffset? updated = null) =>
            (TimeSpan)timeline.Invoke(null, new object[]
            { S(raw), S(start), S(duration), updated ?? epoch, epoch.AddSeconds(age), playing, rate })!;

        Check(Position(10, 30) == S(40), "YouTube stale timeline advances from LastUpdatedTime");
        Check(Position(10, 30.75) - Position(10, 30) == S(0.75),
            "Repeated YouTube polls never rewind the same timeline");
        Check(Position(40, 30, playing: false) == S(40), "YouTube paused position stays frozen");
        Check(Position(40, 2) == S(42), "YouTube resumed timeline advances from its new anchor");
        Check(Position(5, 0.25) == S(5.25) && Position(100, 0.25) == S(100.25),
            "YouTube seeks replace the old position in either direction");
        Check(Position(10, 4, rate: 1.5) == S(16), "YouTube timeline respects playback speed");
        Check(Position(10, 4, rate: 0) == S(10), "YouTube zero playback speed does not advance");
        Check(Position(110, 3, start: 100) == S(13), "YouTube start offset is subtracted once");
        Check(Position(179, 5) == S(180), "YouTube timeline clamps to track duration");
        Check(Position(10, 30, duration: 0) == S(40), "Unknown duration still allows timeline progress");
        Check(Position(10, -5) == S(10), "Future timeline timestamp never rewinds progress");
        Check(Position(10, 30, updated: DateTimeOffset.MinValue) == S(10) &&
              Position(10, 30, updated: new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero)) == S(10),
            "Missing timeline timestamp does not jump to the end");
        Check(Position(-2, 0, playing: false) == TimeSpan.Zero, "Negative timeline position is clamped");

        var snapshotType = type.GetNestedType("PlaybackSnapshot", System.Reflection.BindingFlags.NonPublic)!;
        var estimate = type.GetMethod("EstimatedPosition", flags)!;
        TimeSpan Estimate(bool playing, double position, double rate, double elapsed)
        {
            var snapshot = Activator.CreateInstance(snapshotType,
                "track-id", "track", "artist", S(180), S(position), playing, 0L, rate)!;
            return (TimeSpan)estimate.Invoke(null,
                new[] { snapshot, (object)(long)(System.Diagnostics.Stopwatch.Frequency * elapsed) })!;
        }
        Check(Estimate(true, 40, 1.5, 0.5) == S(40.75), "YouTube inter-poll interpolation respects speed");
        Check(Estimate(false, 40, 1.5, 10) == S(40), "YouTube paused frame interpolation stays frozen");
        Check(Estimate(true, 179, 2, 1) == S(180), "YouTube interpolated frame clamps to duration");
        var lyrics = LrcParser.Parse("[00:10]Old line\n[00:40]Current line\n[00:41]Next line");
        var progress = Estimate(true, Position(10, 30).TotalSeconds, 1, 0.5);
        Check(lyrics.Last(line => line.At <= progress).Text == "Current line",
            "YouTube stale media sample selects the current lyric rather than the old line");
    }

    private static void ParserTests()
    {
        var lines = LrcParser.Parse("[00:01.00]<00:01.00>Hello <00:02.00>world<00:03.00>\n[00:04.00]Next", duration: S(8));
        Check(lines[0].Text == "Hello world" && lines[0].Words.Count == 2, "Enhanced LRC text and words");
        Check(lines[0].Words[1].End == S(3), "Explicit final word end");
        Check(lines[0].Words[0].Progress(S(1.5)) == 0.5 && lines[0].Words[0].Progress(S(0)) == 0 &&
              lines[0].Words[0].Progress(S(4)) == 1, "Word sweep boundaries and midpoint");
        var repeated = LrcParser.Parse("[offset:100]\n[00:01][00:05]<00:01>你<00:02>好<00:03>", duration: S(9));
        Check(repeated[1].Words[0].Start == S(5.1) && repeated[0].Words[1].End == S(3.1), "Repeated timestamps and offset");
        Check(LrcParser.Parse("[00:01]<00:01>A<00:02>B\n[00:04]")[0].Words[1].End == S(4), "Empty instrumental boundary retained");
        Check(LrcParser.Parse("[00:01]<00:01>last")[0].Words.Count == 0, "Missing final boundary degrades to line sync");
        Check(LrcParser.Parse("[00:01]<00:02>B<00:01>A", duration: S(5))[0].Words.Count == 0, "Nonmonotonic LRC degrades");
        Check(LrcParser.Parse("[00:01]plain words")[0].Words.Count == 0, "Line lyrics never fabricate words");
        Check(LrcParser.Parse("[00:01]作词：名字\n[00:02]曲终人散", true).Count == 1, "Legacy credits filter retained");
        var ttml = "<tt xmlns='http://www.w3.org/ns/ttml'><body><div><p begin='00:00:01.000' end='0:04'><span begin='1s' end='2s'>Hello</span> <span begin='2000ms' end='3s'>世</span><span begin='3' end='4'>界</span></p></div></body></tt>";
        var line = TtmlParser.Parse(ttml).Single();
        Check(line.Text == "Hello 世界" && line.Words[0].Text == "Hello " && line.Words.Count == 3, "TTML mixed language and inter-span whitespace");
        var nested = TtmlParser.Parse("<tt xmlns:ttm='urn:test'><p begin='1' end='4'><span><span begin='1' end='2'>A</span></span><span ttm:role='x-bg'><span begin='1' end='3'>BG</span></span></p></tt>").Single();
        Check(nested.Text == "A" && nested.Words.Count == 1, "Nested spans without background duplication");
        var untimed = TtmlParser.Parse("<tt><p begin='1' end='4'>Prefix <span begin='2' end='3'>A</span></p></tt>").Single();
        Check(untimed.Words.Count == 0 && untimed.Text == "Prefix A", "Untimed TTML text preserved as line fallback");
        var grouped = TtmlParser.Parse("<tt><body dur='233s'><div begin='9.731' end='12.105'><p begin='9.731' end='12.105'><span begin='9.731' end='9.927'>Fixture </span><span begin='10' end='11'>text</span></p></div></body></tt>").Single();
        Check(grouped.Words.Count == 2 && grouped.Words[0].Start == S(9.731), "Real provider section bounds do not shift or reject absolute timestamps");
        var invalidWord = TtmlParser.Parse("<tt><p begin='1' end='4'><span begin='1' end='5'>Keep the line</span></p></tt>").Single();
        Check(invalidWord.Text == "Keep the line" && invalidWord.Words.Count == 0, "Bad word interval retains usable line lyrics");
        try { TtmlParser.Parse("<!DOCTYPE tt [<!ENTITY x 'test'>]><tt/>"); throw new Exception("DTD accepted"); }
        catch (System.Xml.XmlException) { Check(true, "XML DTD rejected"); }
        var configured = new List<LyricsSourceOption>
        {
            new() { Source = LyricsSource.NetEase, Enabled = true },
            new() { Source = LyricsSource.Lrclib, Enabled = false }
        };
        var migrated = SettingsStore.NormalizeLyricsSources(configured);
        Check(migrated[0].Source == LyricsSource.NetEase && !migrated[1].Enabled && migrated.Count == 5,
            "Migration preserves existing priority and disabled flags");
        Check(SettingsStore.NormalizeLyricsSources(new List<LyricsSourceOption>
        { new() { Source = LyricsSource.Lrclib, Enabled = false } }).All(p => !p.Enabled), "All-disabled preference survives migration");
        Check(!LyricVersionMatch.CandidateMatches("Side Quest King - Chinese Ver.", "Side Quest King (English Ver.)"), "Explicit language versions are not interchangeable");
        Check(LyricVersionMatch.CandidateMatches("Side Quest King - English Ver.", "Side Quest King (English Version)"), "Equivalent language version labels accepted");
        Check(LyricVersionMatch.Language("Englishman in New York") is null, "Ordinary song titles do not imply a language version");

        var krc = KrcParser.Parse("[1000,2000]<0,300,0>Hello <300,300,0>world\n[4000,1000]<0,500,0>Next");
        Check(krc.Count == 2 && krc[0].Text == "Hello world" && krc[0].Words.Count == 2 &&
              krc[0].Words[0].Start == S(1) && krc[0].Words[0].End == S(1.3) &&
              krc[0].Words[1].Start == S(1.3) && krc[0].Words[1].Text == "world",
            "KRC word offsets are relative to the line start");
        var krcZero = KrcParser.Parse("[0,1000]<0,0,0>A<0,0,0>B<0,500,0>C");
        Check(krcZero[0].Words.Count == 3 && krcZero[0].Words[0].Start == krcZero[0].Words[1].Start,
            "KRC zero-duration and same-start words are preserved");
        var krcOverrun = KrcParser.Parse("[1000,500]<0,300,0>A<300,900,0>B\n[5000,100]C");
        Check(krcOverrun[0].Words.Count == 2, "KRC words may exceed the declared line duration");
        var krcCrossed = KrcParser.Parse("[1000,5000]<0,300,0>A <300,900,0>B\n[1500,100]C");
        Check(krcCrossed[0].Text == "A B" && krcCrossed[0].Words.Count == 0,
            "KRC words past the next line degrade to line sync");
        var krcBackward = KrcParser.Parse("[1000,5000]<500,100,0>A <100,100,0>B");
        Check(krcBackward[0].Words.Count == 0 && krcBackward[0].Text == "A B",
            "KRC backward word offsets degrade to line sync");
        var krcPrefix = KrcParser.Parse("[1000,5000]Intro <0,100,0>A");
        Check(krcPrefix[0].Words.Count == 0 && krcPrefix[0].Text == "Intro A",
            "KRC untimed prefix degrades to line sync");
        Check(KrcParser.Parse("[0,1000]<0,100,0>作词 : 名字\n[2000,1000]<0,100,0>曲终人散", stripCredits: true)
                .Single().Text == "曲终人散",
            "KRC credit lines are stripped");
        Check(KrcParser.Parse("[offset:-500]\n[1000,5000]<0,100,0>A")[0].At == S(0.5),
            "KRC offset tag shifts the timeline");
        Check(KrcParser.Parse("[00:01]Plain LRC").Count == 0, "Plain LRC is not mistaken for KRC");

        Check(LyricsMatchScore.Score("Shape of You", "Ed Sheeran", 233, "Shape of You", "Ed Sheeran", S(233)) == 100,
            "Exact title, artist and duration score full marks");
        Check(LyricsMatchScore.Score("Shape of You", "Ed Sheeran", 300, "Shape of You", "Ed Sheeran", S(233)) == 50,
            "A version far off in duration loses the bonus and is penalised");
        Check(LyricsMatchScore.Score("Shape of You", "Ed Sheeran", 233, "Shape of You", "", S(233)) == 70,
            "Unknown target artist skips the artist bonus");
        Check(LyricsMatchScore.Score("Shape of You", "Ed Sheeran", 0, "Shape of You", "Ed Sheeran", S(233)) == 80,
            "Unknown candidate duration skips the duration bonus");
        Check(LyricsMatchScore.Score("Blinding Lights", "The Weeknd", 200, "Shape of You", "Ed Sheeran", S(233)) == 0,
            "A different title scores zero");

        Check(KugouLyricsProvider.ScoreFilename("周杰伦 - 晴天", 269, "晴天", "周杰伦", S(269)) >= LyricsMatchScore.Minimum,
            "Kugou 'artist - title' filename is matched");
        Check(KugouLyricsProvider.ScoreFilename("hjt - 晴天 - 周杰伦 - hjt", 269, "晴天", "周杰伦", S(269)) >= LyricsMatchScore.Minimum,
            "Kugou filename with extra separators still matches");
        Check(KugouLyricsProvider.ScoreFilename("陈奕迅 - 十年", 200, "晴天", "周杰伦", S(269)) == 0,
            "Kugou unrelated filename scores zero");
    }

    private static async Task ProviderTests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FloatSpotify-tests-" + Guid.NewGuid().ToString("N"));
        var cache = new LyricsCache(directory);
        var words = LrcParser.Parse("[00:01]<00:01>A<00:02>B<00:03>", duration: S(10));
        var lines = LrcParser.Parse("[00:01]Fallback");
        var wordProvider = new FakeProvider(LyricsSource.Karalyr, _ => Task.FromResult(words));
        var lineProvider = new FakeProvider(LyricsSource.Lrclib, _ => Task.FromResult(lines));
        var coordinator = new LyricsCoordinator(new ILyricsProvider[] { wordProvider, lineProvider });
        coordinator.ApplyConfiguration(new[] { LyricsSource.Lrclib, LyricsSource.Karalyr });
        Check((await coordinator.GetAsync("track", "artist", S(10), default))[0].Words.Count == 2, "Word capability upgrades earlier line fallback");
        var failure = new FakeProvider(LyricsSource.Karalyr, _ => throw new JsonException("fixture"));
        coordinator = new LyricsCoordinator(new ILyricsProvider[] { failure, lineProvider });
        coordinator.ApplyConfiguration(new[] { LyricsSource.Karalyr, LyricsSource.Lrclib });
        Check((await coordinator.GetAsync("track", "artist", S(10), default))[0].Text == "Fallback", "Parser error continues fallback");
        await coordinator.GetAsync("another", "artist", S(10), default);
        Check(failure.Calls == 1, "Failed provider cooldown skips repeated requests");
        coordinator = new LyricsCoordinator(new ILyricsProvider[] { failure, lineProvider });
        coordinator.ApplyConfiguration(new[] { LyricsSource.Lrclib, LyricsSource.Karalyr });
        Check((await coordinator.GetAsync("track", "artist", S(10), default))[0].Text == "Fallback", "Failed upgrade retains earlier line result");
        var missing = new FakeProvider(LyricsSource.Karalyr, _ => Task.FromResult<IReadOnlyList<TimedLyric>>(Array.Empty<TimedLyric>()));
        coordinator = new LyricsCoordinator(new[] { missing });
        coordinator.ApplyConfiguration(new[] { LyricsSource.Karalyr });
        await coordinator.GetAsync("missing", "artist", S(10), default);
        await coordinator.GetAsync("missing", "artist", S(10), default);
        Check(missing.Calls == 1, "Negative cache prevents repeated misses");
        var revisionBeforeRefresh = coordinator.Revision;
        coordinator.RequestRefresh();
        await coordinator.GetAsync("missing", "artist", S(10), default);
        Check(missing.Calls == 2 && coordinator.Revision == revisionBeforeRefresh + 1,
            "Manual refresh clears transient misses and invalidates active lyric state");
        coordinator.ApplyConfiguration(Array.Empty<LyricsSource>());
        Check((await coordinator.GetAsync("x", "y", S(10), default)).Count == 0, "All providers disabled");
        coordinator.ApplyConfiguration(new[] { LyricsSource.Karalyr });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await coordinator.GetAsync("x", "y", S(10), cancelled.Token); throw new Exception("Cancellation lost"); }
        catch (OperationCanceledException) { Check(true, "Caller cancellation propagates"); }
        var timeout = new FakeProvider(LyricsSource.Karalyr, async token => { await Task.Delay(10000, token); return words; });
        coordinator = new LyricsCoordinator(new ILyricsProvider[] { timeout, lineProvider });
        coordinator.ApplyConfiguration(new[] { LyricsSource.Karalyr, LyricsSource.Lrclib });
        Check((await coordinator.GetAsync("x", "y", S(10), default))[0].Text == "Fallback", "Per-provider timeout falls through");
        var afterTimeout = await coordinator.GetAsync("timeout-next", "artist", S(10), default);
        Check(timeout.Calls == 2 && afterTimeout[0].Text == "Fallback",
            "Single-source timeout does not cool the source down for other tracks");

        var slowLine = new FakeProvider(LyricsSource.NetEase,
            async token => { await Task.Delay(2500, token); return LrcParser.Parse("[00:01]Slow"); });
        var fastLine = new FakeProvider(LyricsSource.Lrclib, _ => Task.FromResult(LrcParser.Parse("[00:01]Fast")));
        coordinator = new LyricsCoordinator(new ILyricsProvider[] { slowLine, fastLine });
        coordinator.ApplyConfiguration(new[] { LyricsSource.NetEase, LyricsSource.Lrclib });
        var raceWatch = System.Diagnostics.Stopwatch.StartNew();
        var raced = await coordinator.GetAsync("race", "artist", S(10), default);
        raceWatch.Stop();
        Check(raced[0].Text == "Fast" && raceWatch.ElapsedMilliseconds < 1500,
            "Slow peer source does not delay an already-available line result");

        var stalledLine = new FakeProvider(LyricsSource.Lrclib,
            async token => { await Task.Delay(5000, token); return LrcParser.Parse("[00:01]Late lines"); });
        var quickWords = new FakeProvider(LyricsSource.Karalyr, _ => Task.FromResult(words));
        coordinator = new LyricsCoordinator(new ILyricsProvider[] { stalledLine, quickWords });
        coordinator.ApplyConfiguration(new[] { LyricsSource.Lrclib, LyricsSource.Karalyr });
        var wordWatch = System.Diagnostics.Stopwatch.StartNew();
        var fromWords = await coordinator.GetAsync("race-words", "artist", S(10), default);
        wordWatch.Stop();
        Check(fromWords[0].Words.Count > 0 && wordWatch.ElapsedMilliseconds < 1500,
            "Word source is not held back by a slow line source");

        var slowKugou = new FakeProvider(LyricsSource.Kugou,
            async token => { await Task.Delay(600, token); return words; });
        coordinator = new LyricsCoordinator(new ILyricsProvider[] { lineProvider, slowKugou });
        coordinator.ApplyConfiguration(new[] { LyricsSource.Lrclib, LyricsSource.Kugou });
        var upgraded = await coordinator.GetAsync("kugou-upgrade", "artist", S(10), default);
        Check(upgraded[0].Words.Count == 2,
            "Kugou word lyrics are treated as an enhancement and replace the line fallback");

        var pendingEnhancement = new TaskCompletionSource<IReadOnlyList<TimedLyric>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new FakeProvider(LyricsSource.Karalyr, _ => pendingEnhancement.Task);
        var published = new List<IReadOnlyList<TimedLyric>>();
        coordinator = new LyricsCoordinator(new ILyricsProvider[] { delayed, lineProvider });
        coordinator.ApplyConfiguration(new[] { LyricsSource.Karalyr, LyricsSource.Lrclib });
        var pendingLookup = coordinator.GetAsync("provisional", "artist", S(10), default, result => published.Add(result));
        Check(!pendingLookup.IsCompleted && published.Count == 1 && published[0][0].Text == "Fallback",
            "Basic lyrics published before enhancement completes, even when word source is listed first");
        pendingEnhancement.SetResult(words);
        await pendingLookup;
        Check(published.Count == 2 && published[1][0].Words.Count > 0, "Successful word enhancement replaces provisional lines");
        using var plainClient = new HttpClient(new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"syncedLyrics\":null,\"plainLyrics\":\"First line\\nSecond line\"}") }));
        var plainResult = await new LrclibLyricsProvider(plainClient, cache).GetAsync("plain", "artist", S(10), default);
        Check(plainResult.Count == 2 && plainResult.All(line => line.IsEstimatedTiming && !line.IsPlainText) &&
              plainResult[0].Text == "First line" && plainResult[1].Text == "Second line" &&
              plainResult[0].At == TimeSpan.Zero && plainResult[1].At == S(5),
            "Multiline unsynced lyrics use estimated line progression instead of one scrolling block");
        Check(PlainLyricsParser.Parse("Only one line", S(10))[0].IsPlainText,
            "Truly single-line unsynced lyrics retain the plain-text fallback");

        await cache.WriteAsync(LyricsSource.Karalyr, "track", "artist", words, default, S(10));
        var restored = await cache.TryReadAsync(LyricsSource.Karalyr, "track", "artist", default, S(10));
        Check(restored![0].Words[1] == words[0].Words[1], "Disk cache round-trips word times and text");
        Check(await cache.TryReadAsync(LyricsSource.Karalyr, "track", "artist", default, S(15)) is null, "Duration-separated cache keys");
        Check(await cache.TryReadAsync(LyricsSource.BetterLyrics, "track", "artist", default, S(10)) is null, "Provider-separated cache keys");
        var cachePath = Directory.GetFiles(directory, "v6-Karalyr-*.json").Single();
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddDays(-8));
        Check(await cache.TryReadAsync(LyricsSource.Karalyr, "track", "artist", default, S(10)) is null, "Expired cache refreshes");
        await File.WriteAllTextAsync(cachePath, "broken json");
        Check(await cache.TryReadAsync(LyricsSource.Karalyr, "track", "artist", default, S(10)) is null, "Corrupt cache does not break lookup");

        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
        {
            var handler = new FakeHandler(() =>
            {
                var response = new HttpResponseMessage(status);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
                return response;
            });
            using var client = new HttpClient(handler);
            var remote = new WordLyricsProvider(client, LyricsSource.Karalyr, cache);
            coordinator = new LyricsCoordinator(new ILyricsProvider[] { remote, lineProvider });
            coordinator.ApplyConfiguration(new[] { LyricsSource.Karalyr, LyricsSource.Lrclib });
            Check((await coordinator.GetAsync("http", "artist", S(10), default))[0].Text == "Fallback", $"HTTP {(int)status} fallback");
            if (status == HttpStatusCode.TooManyRequests)
            {
                await coordinator.GetAsync("different track", "artist", S(10), default);
                Check(handler.Calls == 1, "429 Retry-After applies across tracks");
            }
        }
        using var successClient = new HttpClient(new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { syncedLyrics = "[00:01]<00:01>Hello<00:02>" }))
        }));
        var successProvider = new WordLyricsProvider(successClient, LyricsSource.Karalyr, cache);
        Check((await successProvider.GetAsync("remote", "artist", S(10), default))[0].Words.Count == 1, "Karalyr response adapter");
        using var lowConfidence = new HttpClient(new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"score\":20,\"ttml\":\"<tt/>\"}")
        }));
        Check((await new WordLyricsProvider(lowConfidence, LyricsSource.BetterLyrics, cache).GetAsync("low", "artist", S(10), default)).Count == 0,
            "Low-confidence TTML rejected");
        using var ttmlClient = new HttpClient(new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { score = 95,
                ttml = "<tt><p begin='1' end='3'><span begin='1' end='2'>Hello</span> <span begin='2' end='3'>world</span></p></tt>" }))
        }));
        Check((await new WordLyricsProvider(ttmlClient, LyricsSource.BetterLyrics, cache).GetAsync("ttml", "artist", S(10), default))[0].Words.Count == 2,
            "Better Lyrics response adapter");
        using var malformedClient = new HttpClient(new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"ttml\":\"<tt>\"}") }));
        coordinator = new LyricsCoordinator(new ILyricsProvider[]
        { new WordLyricsProvider(malformedClient, LyricsSource.BetterLyrics, cache), lineProvider });
        coordinator.ApplyConfiguration(new[] { LyricsSource.BetterLyrics, LyricsSource.Lrclib });
        Check((await coordinator.GetAsync("malformed", "artist", S(10), default))[0].Text == "Fallback", "Malformed TTML response falls through");
        const string chineseTitle = "Side Quest King - Chinese Ver.";
        const string englishTitle = "Side Quest King - English Ver.";
        var chineseLines = LrcParser.Parse("[00:01]中文歌词在这里");
        var englishWords = LrcParser.Parse("[00:01]<00:01>English <00:02>words<00:03>");
        Check(!LyricVersionMatch.TextMatches(chineseTitle, englishWords) && LyricVersionMatch.TextMatches(englishTitle, englishWords),
            "Chinese Ver rejects English-only lyrics while English Ver accepts them");
        coordinator = new LyricsCoordinator(new ILyricsProvider[]
        {
            new FakeProvider(LyricsSource.Lrclib, _ => Task.FromResult(chineseLines)),
            new FakeProvider(LyricsSource.BetterLyrics, _ => Task.FromResult(englishWords))
        });
        coordinator.ApplyConfiguration(new[] { LyricsSource.Lrclib, LyricsSource.BetterLyrics });
        var observed = new List<IReadOnlyList<TimedLyric>>();
        var languageResult = await coordinator.GetAsync(chineseTitle, "artist", S(154), default, observed.Add);
        Check(languageResult[0].Text == "中文歌词在这里" && observed.Count == 1, "Wrong-language word enhancement never replaces correct line lyrics");
        coordinator = new LyricsCoordinator(new ILyricsProvider[]
        {
            new FakeProvider(LyricsSource.Lrclib, _ => Task.FromResult(englishWords)),
            new FakeProvider(LyricsSource.NetEase, _ => Task.FromResult(chineseLines))
        });
        coordinator.ApplyConfiguration(new[] { LyricsSource.Lrclib, LyricsSource.NetEase });
        Check((await coordinator.GetAsync(chineseTitle, "artist", S(154), default))[0].Text == "中文歌词在这里", "Wrong-language basic source continues fallback");
        await cache.WriteAsync(LyricsSource.Karalyr, englishTitle, "artist", englishWords, default, S(154));
        Check(await cache.TryReadAsync(LyricsSource.Karalyr, chineseTitle, "artist", default, S(154)) is null, "Same-duration language versions use separate cache keys");
        await cache.WriteAsync(LyricsSource.Karalyr, chineseTitle, "artist", englishWords, default, S(154));
        Check(await cache.TryReadAsync(LyricsSource.Karalyr, chineseTitle, "artist", default, S(154)) is null, "Wrong-language responses cannot poison cache");
        using var mislabeled = new HttpClient(new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new { trackName = englishTitle, syncedLyrics = "[00:01]English words" })) }));
        Check((await new LrclibLyricsProvider(mislabeled, cache).GetAsync(chineseTitle, "artist", S(154), default)).Count == 0,
            "Provider returned title language mismatch rejected");
        using var plainChinese = new HttpClient(new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new { trackName = chineseTitle, syncedLyrics = "[00:01]English words", plainLyrics = "正确中文歌词" })) }));
        var correctPlain = await new LrclibLyricsProvider(plainChinese, cache).GetAsync(chineseTitle, "artist", S(154), default);
        Check(correctPlain[0].IsPlainText && correctPlain[0].Text == "正确中文歌词", "Correct-language plain text preferred to wrong-language synchronization");

        HttpResponseMessage KugouResponse(HttpRequestMessage request, string content)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("mobileservice.kugou.com"))
            {
                return Json("<!--KG_TAG_RES_START-->" + JsonSerializer.Serialize(new
                {
                    data = new { info = new[] { new { filename = "周杰伦 - 晴天", duration = 269, hash = "HASH-1" } } }
                }) + "<!--KG_TAG_RES_END-->");
            }
            if (url.Contains("krcs.kugou.com"))
            {
                return Json(JsonSerializer.Serialize(new
                {
                    candidates = new[] { new { id = 123, accesskey = "KEY-1", song = "晴天", singer = "周杰伦", duration = 269000 } }
                }));
            }
            return Json(JsonSerializer.Serialize(new { content }));
        }

        using var kugouClient = new HttpClient(new RoutingHandler(request => KugouResponse(request, KrcFixture(
            "[1000,2000]<0,300,0>Hello <300,300,0>world\n[4000,1000]<0,500,0>Next"))));
        var kugou = await new KugouLyricsProvider(kugouClient, cache).GetAsync("晴天", "周杰伦", S(269), default);
        Check(kugou.Count == 2 && kugou[0].Text == "Hello world" &&
              kugou[0].Words.Count == 2 && kugou[0].Words[0].Start == S(1),
            "Kugou three-step chain decodes KRC into word timings");

        var plainCache = new LyricsCache(Path.Combine(Path.GetTempPath(), "FloatSpotify-tests-" + Guid.NewGuid().ToString("N")));
        using var plainKugouClient = new HttpClient(new RoutingHandler(request => KugouResponse(request,
            Convert.ToBase64String(Encoding.UTF8.GetBytes("[00:01]Plain fallback")))));
        var plainKugou = await new KugouLyricsProvider(plainKugouClient, plainCache).GetAsync("晴天", "周杰伦", S(269), default);
        Check(plainKugou.Count == 1 && plainKugou[0].Text == "Plain fallback" && plainKugou[0].Words.Count == 0,
            "Kugou falls back to line lyrics when the payload is not KRC");

        var rejectCache = new LyricsCache(Path.Combine(Path.GetTempPath(), "FloatSpotify-tests-" + Guid.NewGuid().ToString("N")));
        var searchOnlyClient = new HttpClient(new RoutingHandler(request =>
            request.RequestUri!.ToString().Contains("mobileservice.kugou.com")
                ? Json("<!--KG_TAG_RES_START-->" + JsonSerializer.Serialize(new
                {
                    data = new { info = new[] { new { filename = "陈奕迅 - 十年", duration = 200, hash = "HASH-9" } } }
                }) + "<!--KG_TAG_RES_END-->")
                : Json("{\"candidates\":[]}")));
        Check((await new KugouLyricsProvider(searchOnlyClient, rejectCache).GetAsync("晴天", "周杰伦", S(269), default)).Count == 0,
            "Kugou rejects an unrelated search hit instead of fetching wrong lyrics");
    }

    private static async Task DiagnosticsTests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FloatSpotify-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var log = Path.Combine(directory, "lyrics-debug.log");
        LyricsDiagnostics.LogPathOverride = log;

        var words = LrcParser.Parse("[00:01]<00:01>A<00:02>B<00:03>", duration: S(10));
        var lines = LrcParser.Parse("[00:01]Fallback");

        try
        {
            Environment.SetEnvironmentVariable(LyricsDiagnostics.EnvironmentVariable, null);
            var wordProvider = new FakeProvider(LyricsSource.Kugou, _ => Task.FromResult(words));
            var lineProvider = new FakeProvider(LyricsSource.Lrclib, _ => Task.FromResult(lines));
            var coordinator = new LyricsCoordinator(new ILyricsProvider[] { lineProvider, wordProvider });
            coordinator.ApplyConfiguration(new[] { LyricsSource.Lrclib, LyricsSource.Kugou });
            await coordinator.GetAsync("quiet", "artist", S(10), default);
            Check(!File.Exists(log), "Lyrics diagnostics stay silent unless the environment variable is set");

            Environment.SetEnvironmentVariable(LyricsDiagnostics.EnvironmentVariable, "1");
            await coordinator.GetAsync("晴天", "周杰伦", S(269), default);
            var text = await File.ReadAllTextAsync(log);
            Check(text.Contains("晴天 / 周杰伦") && text.Contains("Kugou") && text.Contains("<- WINNER"),
                "Lyrics diagnostics name the track and the winning source");
            Check(text.Contains("Lrclib") && text.Contains(LyricsOutcome.Hit),
                "Lyrics diagnostics list every source that ran");
            Console.WriteLine("--- lyrics-debug.log 实际内容 ---");
            Console.WriteLine(text.TrimEnd());
            Console.WriteLine("--------------------------------");

            var missing = new FakeProvider(LyricsSource.NetEase,
                _ => Task.FromResult<IReadOnlyList<TimedLyric>>(Array.Empty<TimedLyric>()));
            var repeatCoordinator = new LyricsCoordinator(new ILyricsProvider[] { missing, lineProvider });
            repeatCoordinator.ApplyConfiguration(new[] { LyricsSource.NetEase, LyricsSource.Lrclib });
            await repeatCoordinator.GetAsync("repeat", "artist", S(10), default);
            await repeatCoordinator.GetAsync("repeat", "artist", S(10), default);
            var repeatText = await File.ReadAllTextAsync(log);
            Check(repeatText.Contains(LyricsOutcome.NotFound) && repeatText.Contains(LyricsOutcome.Remembered),
                "Lyrics diagnostics distinguish 'found nothing' from 'skipped, remembered miss'");

            var slowLine = new FakeProvider(LyricsSource.BetterLyrics,
                async token => { await Task.Delay(2000, token); return lines; });
            var fastWords = new FakeProvider(LyricsSource.Kugou, _ => Task.FromResult(words));
            var earlyStop = new LyricsCoordinator(new ILyricsProvider[] { slowLine, fastWords });
            earlyStop.ApplyConfiguration(new[] { LyricsSource.BetterLyrics, LyricsSource.Kugou });
            await earlyStop.GetAsync("abandon", "artist", S(10), default);
            var abandonText = await File.ReadAllTextAsync(log);
            Check(abandonText.Contains(LyricsOutcome.Abandoned),
                "Lyrics diagnostics record sources abandoned when the race stops early");
            Check(abandonText.Contains("BetterLyrics " + LyricsOutcome.Abandoned),
                "Lyrics diagnostics keep the longest source name separated from its outcome");

            Environment.SetEnvironmentVariable(LyricsDiagnostics.EnvironmentVariable, null);
            Check(!LyricsDiagnostics.IsEnabled, "Lyrics diagnostics are off with neither switch present");
            await File.WriteAllTextAsync(LyricsDiagnostics.MarkerPath, string.Empty);
            Check(LyricsDiagnostics.IsEnabled, "Lyrics diagnostics can be enabled by a marker file");
            LyricsDiagnostics.AnnounceStartup();
            var markerText = await File.ReadAllTextAsync(log);
            Check(markerText.Contains("启动") && markerText.Contains("开关文件"),
                "Lyrics diagnostics announce startup and name the switch that enabled them");
            Check(markerText.Contains("还没播放歌曲"), "Lyrics diagnostics explain why no fetch is logged yet");
            File.Delete(LyricsDiagnostics.MarkerPath);
            Check(!LyricsDiagnostics.IsEnabled, "Deleting the marker file turns lyrics diagnostics back off");

            Environment.SetEnvironmentVariable(LyricsDiagnostics.EnvironmentVariable, "1");
            await File.WriteAllTextAsync(log, new string('x', (2 * 1024 * 1024) + 1));
            await coordinator.GetAsync("rotate", "artist", S(10), default);
            Check(File.Exists(log + ".1") && new FileInfo(log).Length < 2 * 1024 * 1024,
                "Lyrics diagnostics rotate the log instead of growing forever");
        }
        finally
        {
            Environment.SetEnvironmentVariable(LyricsDiagnostics.EnvironmentVariable, null);
            if (File.Exists(LyricsDiagnostics.MarkerPath))
                File.Delete(LyricsDiagnostics.MarkerPath);
            LyricsDiagnostics.LogPathOverride = null;
        }
    }

    private static async Task LiveDiagnostics()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FloatSpotify-livediag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var log = Path.Combine(directory, "lyrics-debug.log");
        LyricsDiagnostics.LogPathOverride = log;
        Environment.SetEnvironmentVariable(LyricsDiagnostics.EnvironmentVariable, "1");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FloatSpotify/2.0");
        var cache = new LyricsCache(Path.Combine(directory, "cache"));

        var coordinator = new LyricsCoordinator(new ILyricsProvider[]
        {
            new LrclibLyricsProvider(http, cache),
            new WordLyricsProvider(http, LyricsSource.Karalyr, cache),
            new WordLyricsProvider(http, LyricsSource.BetterLyrics, cache),
            new NetEaseLyricsProvider(http),
            new KugouLyricsProvider(http, cache)
        });
        coordinator.ApplyConfiguration(new[]
        {
            LyricsSource.Lrclib, LyricsSource.NetEase, LyricsSource.Kugou,
            LyricsSource.Karalyr, LyricsSource.BetterLyrics
        });

        foreach (var (track, artist, seconds) in new[]
        {
            ("晴天", "周杰伦", 269),
            ("Good Time", "Owl City", 205),
            ("Yesterday", "The Beatles", 125)
        })
        {
            var result = await coordinator.GetAsync(track, artist, S(seconds), default);
            Console.WriteLine($"  {track} / {artist}: {result.Count} lines, {result.Count(line => line.Words.Count > 0)} worded");
        }

        Console.WriteLine();
        Console.WriteLine(await File.ReadAllTextAsync(log));
    }

    private static async Task LiveLyricsCheck()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FloatSpotify/2.0");
        var cache = new LyricsCache(Path.Combine(Path.GetTempPath(), "FloatSpotify-live-" + Guid.NewGuid().ToString("N")));
        var baseline = await new LrclibLyricsProvider(http, cache).GetAsync("Good Time", "Owl City", S(205), default);
        Check(baseline.Any(line => !line.IsPlainText && !string.IsNullOrWhiteSpace(line.Text)), "LIVE Good Time / Owl City: LRCLIB line-synced lyrics");
        var upgraded = await new WordLyricsProvider(http, LyricsSource.BetterLyrics, cache).GetAsync("Shape of You", "Ed Sheeran", S(233), default);
        Check(upgraded.Any(line => line.Words.Count > 0), "LIVE grouped TTML: current provider response parsed with word timings");
        var coordinator = new LyricsCoordinator(new ILyricsProvider[]
        { new LrclibLyricsProvider(http, cache), new WordLyricsProvider(http, LyricsSource.Karalyr, cache), new WordLyricsProvider(http, LyricsSource.BetterLyrics, cache) });
        coordinator.ApplyConfiguration(new[] { LyricsSource.Karalyr, LyricsSource.BetterLyrics, LyricsSource.Lrclib });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var final = await coordinator.GetAsync("Good Time", "Owl City", S(205), default,
            result => Console.WriteLine($"LIVE Good Time available at {timer.ElapsedMilliseconds}ms; lines={result.Count}; wordLines={result.Count(line => line.Words.Count > 0)}"));
        Check(final.Any(line => !string.IsNullOrWhiteSpace(line.Text)), "LIVE Good Time keeps lyrics after enhancement requests");

        var kugouTimer = System.Diagnostics.Stopwatch.StartNew();
        var kugou = await new KugouLyricsProvider(http, cache).GetAsync("晴天", "周杰伦", S(269), default);
        kugouTimer.Stop();
        var wordLines = kugou.Count(line => line.Words.Count > 0);
        Console.WriteLine($"LIVE Kugou 晴天: {kugouTimer.ElapsedMilliseconds}ms; lines={kugou.Count}; wordLines={wordLines}");
        Check(wordLines > 0, "LIVE Kugou: KRC word timings for 晴天 / 周杰伦");

        var kugouPlain = await new KugouLyricsProvider(http, cache).GetAsync("Good Time", "Owl City", S(205), default);
        Console.WriteLine($"LIVE Kugou Good Time: lines={kugouPlain.Count}; wordLines={kugouPlain.Count(line => line.Words.Count > 0)}");
        Check(kugouPlain.Any(line => !string.IsNullOrWhiteSpace(line.Text)), "LIVE Kugou: English track resolves too");
    }

    private static void PlaybackAvailabilityTest()
    {
        Check(SpotifyPlaybackEngine.NetworkRetryDelay(1) == S(5) &&
              SpotifyPlaybackEngine.NetworkRetryDelay(2) == S(10) &&
              SpotifyPlaybackEngine.NetworkRetryDelay(3) == S(20) &&
              SpotifyPlaybackEngine.NetworkRetryDelay(4) == S(30) &&
              SpotifyPlaybackEngine.NetworkRetryDelay(20) == S(30),
            "Spotify network polling backs off from five seconds and caps at thirty seconds");

        // Exercise the actual Spotify frame path without authenticating or polling a player.
        using var engine = new SpotifyPlaybackEngine(() => null, new LyricsCoordinator(Array.Empty<ILyricsProvider>()));
        var type = typeof(SpotifyPlaybackEngine);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        void Set(string name, object value) => type.GetField(name, flags)!.SetValue(engine, value);
        var snapshotType = type.GetNestedType("PlaybackSnapshot", System.Reflection.BindingFlags.NonPublic)!;
        var snapshot = Activator.CreateInstance(snapshotType, "fixture-id", "Good Time", "Owl City", S(205), S(10), false,
            System.Diagnostics.Stopwatch.GetTimestamp())!;
        Set("_snapshot", snapshot);
        Set("_accessToken", "fixture-not-used-for-network");
        Set("_lyricsTrackId", "fixture-id");
        Set("_lyrics", new[] { new TimedLyric(S(1), "Available lyric") });
        Set("_lyricsTask", new TaskCompletionSource<IReadOnlyList<TimedLyric>>().Task);
        PlaybackFrame Frame() => (PlaybackFrame)type.GetMethod("CreateFrame", flags)!.Invoke(engine, null)!;
        Check(Frame().CurrentLine == "Available lyric" && Frame().ActiveLyric is not null,
            "Spotify renders provisional lyrics while word lookup is pending");
        Set("_lyricsTask", Task.FromException<IReadOnlyList<TimedLyric>>(new HttpRequestException("fixture")));
        type.GetMethod("ObserveLyricsTask", flags)!.Invoke(engine, null);
        Check(Frame().CurrentLine == "Available lyric", "Spotify retains displayed lyrics when enrichment task faults");
        Set("_lastSuccessfulPollAt", DateTimeOffset.UtcNow.Subtract(S(16)));
        type.GetMethod("RecordPlaybackPollFailure", flags)!.Invoke(engine, null);
        Check(Frame().Track == "Spotify" && Frame().ActiveLyric is null,
            "Spotify drops stale lyrics after a sustained network outage");
    }

    private static void RenderTests(string directory)
    {
        Directory.CreateDirectory(directory);
        var line = LrcParser.Parse("[00:01]<00:01>Hello <00:02>世界 <00:03>sing along<00:05>", duration: S(6))[0];
        var text = new KaraokeText { FontSize = 46, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.White };
        byte[] Render(double time, string file, double width = 480, TimedLyric? lyric = null)
        {
            lyric ??= line;
            text.Frame = new PlaybackFrame("fixture", "fixture", lyric.Text, "", false, S(time), S(6), ActiveLyric: lyric);
            text.Measure(new Size(width, 500));
            text.Arrange(new Rect(0, 0, width, text.DesiredSize.Height));
            text.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)width, Math.Max(1, (int)Math.Ceiling(text.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen())
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 24, 30)), null,
                    new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            bitmap.Render(background);
            bitmap.Render(text);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(directory, file))) encoder.Save(stream);
            var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
            return bytes;
        }
        var before = Render(0, "01-before.png");
        var middle = Render(1.5, "02-mid-word.png");
        var end = Render(5, "03-complete.png");
        long Alpha(byte[] bytes) => bytes.Where((_, i) => i % 4 != 3).Sum(b => (long)b);
        Check(Alpha(before) > 0 && Alpha(before) < Alpha(middle) && Alpha(middle) < Alpha(end), "Rendered highlight grows across word timeline");
        Check(middle.SequenceEqual(Render(1.5, "04-paused.png")), "Paused frame pixels stable");
        Check(before.SequenceEqual(Render(0, "05-seek-back.png")), "Backward seek resets highlight");
        Render(3.5, "06-wrapped.png", 190);
        Check(text.ActualHeight > 80, "Narrow overlay wraps without dropping words");
        var plain = new TimedLyric(S(0), "Line fallback");
        Check(Render(0, "07-line.png", lyric: plain).SequenceEqual(Render(5, "08-line-later.png", lyric: plain)), "Line fallback has no fake sweep");
        Console.WriteLine("Render evidence: " + Path.GetFullPath(directory));
    }

    private sealed class FakeProvider(LyricsSource source, Func<CancellationToken, Task<IReadOnlyList<TimedLyric>>> fetch) : ILyricsProvider
    {
        public LyricsSource Source => source;
        public int Calls { get; private set; }
        public Task<IReadOnlyList<TimedLyric>> GetAsync(string track, string artist, TimeSpan duration, CancellationToken token)
        { Calls++; return fetch(token); }
    }
    private sealed class FakeHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(response()); }
    }
    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(route(request)); }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string KrcFixture(string plain)
    {
        byte[] key =
        [
            0x40, 0x47, 0x61, 0x77, 0x5E, 0x32, 0x74, 0x47,
            0x51, 0x36, 0x31, 0x2D, 0xCE, 0xD2, 0x6E, 0x69
        ];

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(deflate, new UTF8Encoding(false)))
            writer.Write(plain);

        var body = compressed.ToArray();
        var bytes = new byte[body.Length + 4];
        bytes[0] = (byte)'k';
        bytes[1] = (byte)'r';
        bytes[2] = (byte)'c';
        bytes[3] = (byte)'1';
        for (var index = 0; index < body.Length; index++)
            bytes[index + 4] = (byte)(body[index] ^ key[index % key.Length]);

        return Convert.ToBase64String(bytes);
    }
}
