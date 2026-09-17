using System.Globalization;
using System.IO;
using System.Text;

namespace FloatSpotify.Playback;

/// <summary>
/// 取词诊断：把「这一次到底哪个源在供词、其他源为什么没赢」写进日志文件。
/// <para>
/// <b>默认完全关闭</b>。两种开启方式，任选其一：
/// <list type="number">
/// <item>环境变量 <c>FLOATSPOTIFY_LYRICS_DEBUG</c>（值为 1 / true / yes / on）——
///   注意它必须**在启动本程序的那个进程里**设置，从开始菜单启动是继承不到的；</item>
/// <item>开关文件 <c>%LocalAppData%\FloatSpotify.Next\lyrics-debug.on</c> ——
///   建个空文件就行，不用管怎么启动的，删掉即关闭。</item>
/// </list>
/// 两种方式都是「普通用户不可能看到」的：既不出现在界面上，也不出现在设置里。
/// 关闭时这里只做一次环境变量读取 + 一次文件探测，不写任何东西。
/// </para>
/// <para>
/// 日志位置：<c>%LocalAppData%\FloatSpotify.Next\lyrics-debug.log</c>。
/// 实时观察（PowerShell）：
/// <c>Get-Content -Wait -Tail 40 "$env:LocalAppData\FloatSpotify.Next\lyrics-debug.log"</c>
/// </para>
/// <para>
/// 任何异常都被吞掉：诊断绝不能影响播放。
/// </para>
/// </summary>
internal static class LyricsDiagnostics
{
    /// <summary>设为 1 / true / yes / on 即开启。</summary>
    internal const string EnvironmentVariable = "FLOATSPOTIFY_LYRICS_DEBUG";

    private const string FileName = "lyrics-debug.log";
    private const string MarkerFileName = "lyrics-debug.on";
    private const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>
    /// 低于这个耗时几乎一定是磁盘缓存命中。实测真实网络往返最快也在 38ms 以上，
    /// 而缓存命中是 0-6ms，所以 15ms 能把两者干净地分开。仍然只是推测，所以日志里带问号。
    /// </summary>
    private const long CacheHitMilliseconds = 15;

    private static readonly object Gate = new();
    private static string? _logPathOverride;

    /// <summary>
    /// 测试用的重定向点。<c>Environment.GetFolderPath</c> 走的是 Win32 API，改环境变量没用，
    /// 而测试绝不该往用户真实的 <c>%LocalAppData%</c> 里写日志。开关文件也跟着一起重定向，
    /// 这样测试只需要改这一个地方。
    /// </summary>
    internal static string? LogPathOverride
    {
        get => _logPathOverride;
        set => _logPathOverride = value;
    }

    private static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FloatSpotify.Next");

    internal static string LogPath => _logPathOverride ?? Path.Combine(AppDataDirectory, FileName);

    /// <summary>开关文件路径。存在即开启。</summary>
    internal static string MarkerPath => _logPathOverride is null
        ? Path.Combine(AppDataDirectory, MarkerFileName)
        : Path.Combine(Path.GetDirectoryName(_logPathOverride) ?? ".", MarkerFileName);

    internal static bool IsEnabled => EnabledBy is not null;

    /// <summary>
    /// 为什么开启（会写进日志）。关闭时返回 null。
    /// 每次取词都重新判断，这样用户不必重启就能改。
    /// </summary>
    internal static string? EnabledBy
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (value is not null &&
                (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                 value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                 value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                 value.Equals("on", StringComparison.OrdinalIgnoreCase)))
            {
                return $"环境变量 {EnvironmentVariable}={value}";
            }

            try
            {
                if (File.Exists(MarkerPath))
                    return $"开关文件 {MarkerPath}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Lyrics diagnostics marker check failed: {ex.GetType().Name}.");
            }

            return null;
        }
    }

    /// <summary>
    /// 启动时写一行「诊断已开启」。
    /// <para>
    /// 这一步不只是好看：没有它的话，用户设好开关、启动程序，却发现日志文件根本不存在
    /// （因为还没播放任何歌曲，一次取词都没发生），会以为开关没生效。
    /// 先落一行就能立刻确认「机制通了」，剩下的只是等一首歌。
    /// </para>
    /// </summary>
    internal static void AnnounceStartup()
    {
        var reason = EnabledBy;
        if (reason is null)
            return;

        var version = typeof(LyricsDiagnostics).Assembly.GetName().Version?.ToString() ?? "?";
        WriteRaw(
            "# 启动 " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
            "  v" + version +
            "  开启方式：" + reason + Environment.NewLine +
            "# 还没播放歌曲时这里不会有取词记录 —— 那是正常的。" + Environment.NewLine +
            Environment.NewLine);
    }

    internal static void Write(LyricsFetchReport report)
    {
        if (!IsEnabled)
            return;

        WriteRaw(Format(report));
    }

    private static void WriteRaw(string text)
    {
        try
        {
            lock (Gate)
            {
                var path = LogPath;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var isNew = !File.Exists(path);
                if (!isNew && new FileInfo(path).Length > MaxBytes)
                {
                    // 简单的单份轮转：留着上一份比无限增长好，也不需要用户手动清理。
                    File.Move(path, path + ".1", overwrite: true);
                    isNew = true;
                }

                // 带 BOM 时 Windows 记事本才会正确识别 UTF-8（歌名歌手都是中文）。
                // AppendAllText 只在文件从零开始写时才落 BOM，所以这里按 isNew 决定。
                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: isNew);
                if (isNew)
                    File.AppendAllText(path, Header(), encoding);
                File.AppendAllText(path, text, encoding);
            }
        }
        catch (Exception ex)
        {
            // 诊断失败绝不能影响播放。
            System.Diagnostics.Trace.WriteLine($"Lyrics diagnostics failed: {ex.GetType().Name}.");
        }
    }

    private static string Format(LyricsFetchReport report)
    {
        var builder = new StringBuilder();
        var stamp = DateTimeOffset.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        builder.Append('[').Append(stamp).Append("] ")
               .Append(report.Track).Append(" / ").Append(report.Artist)
               .Append(" (").Append(report.DurationSeconds.ToString("0", CultureInfo.InvariantCulture)).Append("s)")
               .Append("  ->  ");

        if (report.Winner is { } winner)
        {
            builder.Append(winner).Append(' ').Append(QualityName(report.WinnerQuality))
                   .Append(' ').Append(report.WinnerMilliseconds.ToString(CultureInfo.InvariantCulture)).Append("ms");
        }
        else
        {
            builder.Append("NO LYRICS");
        }

        // 整段耗时明显大于首词耗时，说明歌词上屏之后还在等更慢的增强源。
        // 这正是「明明有歌词却出得慢」的形态：首词时间要看 winner，而不是看总耗时。
        if (report.TotalMilliseconds > report.WinnerMilliseconds + 50)
        {
            builder.Append("  (total ")
                   .Append(report.TotalMilliseconds.ToString(CultureInfo.InvariantCulture))
                   .Append("ms)");
        }

        builder.AppendLine();

        foreach (var attempt in report.Attempts)
        {
            // 源名最长的 "BetterLyrics" 有 11 个字符，列宽必须大于它，否则会跟结果粘在一起。
            builder.Append("            ")
                   .Append(attempt.Source.ToString().PadRight(13))
                   .Append(attempt.Outcome.PadRight(15));

            // 没发出请求的源（冷却 / 记忆跳过 / 收手时还在跑）不该显示「0ms」，那会被读成「瞬间返回」。
            builder.Append(MadeRequest(attempt.Outcome)
                ? attempt.Milliseconds.ToString(CultureInfo.InvariantCulture).PadLeft(6) + "ms"
                : "      --");

            if (attempt.Lines > 0)
            {
                builder.Append("  ").Append(attempt.Lines).Append(" lines");
                if (attempt.WordLines > 0)
                    builder.Append(" / ").Append(attempt.WordLines).Append(" worded");
            }

            // 网络往返不可能这么快，所以这几乎一定是磁盘缓存命中。
            if (attempt.Outcome == LyricsOutcome.Hit && attempt.Milliseconds <= CacheHitMilliseconds)
                builder.Append("   (cache?)");

            if (attempt.Source == report.Winner)
                builder.Append("   <- WINNER");

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Header() =>
        "# FloatSpotify 歌词取词诊断日志" + Environment.NewLine +
        "# 开启方式：环境变量 " + EnvironmentVariable + "=1，或在此目录建一个 " + MarkerFileName + "。" + Environment.NewLine +
        "# 删除本文件不影响程序运行。" + Environment.NewLine +
        "# 每首歌一段：第一行是结论（最终采用哪个源），随后每行是一个源的尝试结果。" + Environment.NewLine +
        "# 实时观察：Get-Content -Wait -Tail 40 \"$env:LocalAppData\\FloatSpotify.Next\\" + FileName + "\"" + Environment.NewLine +
        Environment.NewLine;

    private static string QualityName(int quality) => quality switch
    {
        >= 2 => "word",
        1 => "line",
        _ => "plain"
    };

    /// <summary>这个结果意味着「真的发出过一次取词请求」，才有耗时可言。</summary>
    private static bool MadeRequest(string outcome) =>
        outcome is LyricsOutcome.Hit or LyricsOutcome.NotFound or LyricsOutcome.Timeout
            or LyricsOutcome.WrongLanguage ||
        outcome.StartsWith("error:", StringComparison.Ordinal);
}

/// <summary>单个源这一次的尝试结果。<see cref="Outcome"/> 取值见 <see cref="LyricsOutcome"/>。</summary>
internal sealed record LyricsAttempt(
    LyricsSource Source,
    string Outcome,
    long Milliseconds,
    int Lines,
    int WordLines);

/// <summary>一次取词的完整诊断。</summary>
internal sealed record LyricsFetchReport(
    string Track,
    string Artist,
    double DurationSeconds,
    LyricsSource? Winner,
    int WinnerQuality,
    long WinnerMilliseconds,
    long TotalMilliseconds,
    IReadOnlyList<LyricsAttempt> Attempts);

/// <summary>
/// 尝试结果的取值。用常量而不是 enum，是为了能表达 <c>error:HttpRequestException</c> 这种带细节的值。
/// </summary>
internal static class LyricsOutcome
{
    /// <summary>拿到了可用的行级 / 逐字歌词。</summary>
    internal const string Hit = "hit";

    /// <summary>源明确回答「这首歌没有歌词」。</summary>
    internal const string NotFound = "not-found";

    /// <summary>单源超时。注意这不等于源坏了，只影响这一次。</summary>
    internal const string Timeout = "timeout";

    /// <summary>取到的歌词语种和曲名标注的版本对不上，主动丢弃。</summary>
    internal const string WrongLanguage = "wrong-language";

    /// <summary>整个链路被调用方取消（换歌 / 退出）。</summary>
    internal const string Cancelled = "cancelled";

    /// <summary>这个源正在源级冷却中（之前吃过 429 / 5xx），这次没发请求。</summary>
    internal const string Cooling = "cooling";

    /// <summary>这首歌在这个源上刚失败过，短时间内不再重试。</summary>
    internal const string Remembered = "remembered-miss";

    /// <summary>竞速已经收手时它还在跑，没等它。</summary>
    internal const string Abandoned = "abandoned";

    /// <summary>异常，后面接异常类型名。</summary>
    internal static string Error(string typeName) => "error:" + typeName;
}
