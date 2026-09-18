using System.Globalization;
using System.IO;
using System.Text;

namespace FloatSpotify.Playback;

internal static class LyricsDiagnostics
{
    internal const string EnvironmentVariable = "FLOATSPOTIFY_LYRICS_DEBUG";

    private const string FileName = "lyrics-debug.log";
    private const string MarkerFileName = "lyrics-debug.on";
    private const long MaxBytes = 2 * 1024 * 1024;

    private const long CacheHitMilliseconds = 15;

    private static readonly object Gate = new();
    private static string? _logPathOverride;

    internal static string? LogPathOverride
    {
        get => _logPathOverride;
        set => _logPathOverride = value;
    }

    private static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FloatSpotify.Next");

    internal static string LogPath => _logPathOverride ?? Path.Combine(AppDataDirectory, FileName);

    internal static string MarkerPath => _logPathOverride is null
        ? Path.Combine(AppDataDirectory, MarkerFileName)
        : Path.Combine(Path.GetDirectoryName(_logPathOverride) ?? ".", MarkerFileName);

    internal static bool IsEnabled => EnabledBy is not null;

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
                    File.Move(path, path + ".1", overwrite: true);
                    isNew = true;
                }

                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: isNew);
                if (isNew)
                    File.AppendAllText(path, Header(), encoding);
                File.AppendAllText(path, text, encoding);
            }
        }
        catch (Exception ex)
        {
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

        if (report.TotalMilliseconds > report.WinnerMilliseconds + 50)
        {
            builder.Append("  (total ")
                   .Append(report.TotalMilliseconds.ToString(CultureInfo.InvariantCulture))
                   .Append("ms)");
        }

        builder.AppendLine();

        foreach (var attempt in report.Attempts)
        {
            builder.Append("            ")
                   .Append(attempt.Source.ToString().PadRight(13))
                   .Append(attempt.Outcome.PadRight(15));

            builder.Append(MadeRequest(attempt.Outcome)
                ? attempt.Milliseconds.ToString(CultureInfo.InvariantCulture).PadLeft(6) + "ms"
                : "      --");

            if (attempt.Lines > 0)
            {
                builder.Append("  ").Append(attempt.Lines).Append(" lines");
                if (attempt.WordLines > 0)
                    builder.Append(" / ").Append(attempt.WordLines).Append(" worded");
            }

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

    private static bool MadeRequest(string outcome) =>
        outcome is LyricsOutcome.Hit or LyricsOutcome.NotFound or LyricsOutcome.Timeout
            or LyricsOutcome.WrongLanguage ||
        outcome.StartsWith("error:", StringComparison.Ordinal);
}

internal sealed record LyricsAttempt(
    LyricsSource Source,
    string Outcome,
    long Milliseconds,
    int Lines,
    int WordLines);

internal sealed record LyricsFetchReport(
    string Track,
    string Artist,
    double DurationSeconds,
    LyricsSource? Winner,
    int WinnerQuality,
    long WinnerMilliseconds,
    long TotalMilliseconds,
    IReadOnlyList<LyricsAttempt> Attempts);

internal static class LyricsOutcome
{
    internal const string Hit = "hit";

    internal const string NotFound = "not-found";

    internal const string Timeout = "timeout";

    internal const string WrongLanguage = "wrong-language";

    internal const string Cancelled = "cancelled";

    internal const string Cooling = "cooling";

    internal const string Remembered = "remembered-miss";

    internal const string Abandoned = "abandoned";

    internal static string Error(string typeName) => "error:" + typeName;
}
