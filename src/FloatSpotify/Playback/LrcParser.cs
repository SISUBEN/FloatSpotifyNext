using System.Globalization;
using System.Text.RegularExpressions;

namespace FloatSpotify.Playback;

/// <summary>
/// LRC 文本解析。LRCLIB 和网易云返回的都是标准 LRC，所以两边共用这一份实现。
/// </summary>
internal static class LrcParser
{
    private static readonly Regex WordPattern = new(
        @"<(?<minutes>\d{1,3}):(?<seconds>\d{2}(?:\.\d{1,3})?)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TimestampPattern = new(
        @"\[(?<minutes>\d{1,3}):(?<seconds>\d{2}(?:\.\d{1,3})?)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OffsetPattern = new(
        @"\[\s*offset\s*:\s*(?<milliseconds>[+-]?\d+)\s*\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// 制作人员署名行。网易云会把「作词 : 黄家驹」「混音 : Shunichi Yokoi」这类信息
    /// 也带上时间戳塞进歌词里，不过滤的话开头的曲目信息会当成歌词显示出来。
    /// <para>
    /// 关键词后面**必须**跟冒号才算署名。这一点很关键 —— 否则「曲终人散」「词不达意」
    /// 这种真实歌词会被误杀。
    /// </para>
    /// </summary>
    private static readonly Regex CreditPattern = new(
        @"^\s*(作词|作曲|编曲|作词人|作曲人|制作人|出品人|监制|统筹|企划|宣传|发行|出品|制作|" +
        @"录音|录音师|录音室|混音|混音师|母带|母带工程师|母带处理|" +
        @"吉他|贝斯|鼓|键盘|弦乐|和声|配唱|人声|美术|设计|" +
        @"词曲|词|曲|OP|SP)\s*[:：]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// 判断一行歌词是不是制作人员署名。KRC 和 LRC 的署名行长得一样，
    /// 所以 <see cref="KrcParser"/> 也共用这一份规则。
    /// </summary>
    public static bool IsCreditLine(string? text) =>
        !string.IsNullOrWhiteSpace(text) && CreditPattern.IsMatch(text);

    /// <summary>
    /// 解析 LRC 文本。无法解析或没有可用行时返回空集合。
    /// </summary>
    /// <param name="stripCredits">
    /// 是否丢弃制作人员署名行。网易云传 true；LRCLIB 传 false 保持原有行为不变。
    /// </param>
    public static IReadOnlyList<TimedLyric> Parse(string? lrc, bool stripCredits = false, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(lrc))
            return Array.Empty<TimedLyric>();

        var sourceLines = lrc.Split(
            ["\r\n", "\n", "\r"],
            StringSplitOptions.None);

        // LRC 允许用一行 [offset:±ms] 整体平移时间轴，先扫一遍拿到它。
        var offsetMilliseconds = 0;
        foreach (var sourceLine in sourceLines)
        {
            var offsetMatch = OffsetPattern.Match(sourceLine);
            if (offsetMatch.Success &&
                int.TryParse(
                    offsetMatch.Groups["milliseconds"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedOffset))
            {
                offsetMilliseconds = parsedOffset;
            }
        }

        var offset = TimeSpan.FromMilliseconds(offsetMilliseconds);
        var result = new List<TimedLyric>();
        foreach (var line in sourceLines)
        {
            var matches = TimestampPattern.Matches(line);
            if (matches.Count == 0)
                continue;

            // 一行可以有多个时间戳（副歌复用），正文统一取最后一个时间戳之后的部分。
            var lastMatch = matches[matches.Count - 1];
            var rawText = line[(lastMatch.Index + lastMatch.Length)..].Trim();
            var text = WordPattern.Replace(rawText, string.Empty);
            if (stripCredits && CreditPattern.IsMatch(text))
                continue;

            foreach (Match match in matches)
            {
                if (!int.TryParse(
                        match.Groups["minutes"].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var minutes) ||
                    !double.TryParse(
                        match.Groups["seconds"].Value,
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var seconds))
                {
                    continue;
                }

                var at = TimeSpan.FromSeconds(minutes * 60 + seconds) + offset;
                var words = new List<TimedWord>();
                var wordMatches = WordPattern.Matches(rawText);
                // Repeated line timestamps shift the same word timeline with the line.
                var shift = at - ReadTime(matches[0]);
                var prefixLength = wordMatches.Count > 0 ? wordMatches[0].Index : 0;
                for (var i = 0; i < wordMatches.Count; i++)
                {
                    var wordMatch = wordMatches[i];
                    var start = ReadTime(wordMatch) + shift;
                    var end = i + 1 < wordMatches.Count ? ReadTime(wordMatches[i + 1]) + shift : start;
                    var endIndex = i + 1 < wordMatches.Count ? wordMatches[i + 1].Index : rawText.Length;
                    var wordText = rawText[(wordMatch.Index + wordMatch.Length)..endIndex];
                    if (wordText.Length > 0)
                        words.Add(new TimedWord(start, end, wordText));
                }
                // Untimed prefixes or nonmonotonic timestamps degrade to line sync.
                if (prefixLength > 0 || words.Any(w => w.Start < at || w.End < w.Start) ||
                    string.Concat(words.Select(w => w.Text)) != text ||
                    words.Zip(words.Skip(1)).Any(pair => pair.First.Start >= pair.Second.Start))
                    words.Clear();
                result.Add(new TimedLyric(
                    at < TimeSpan.Zero ? TimeSpan.Zero : at,
                    text) { Words = words });
            }
        }

        var sorted = result
            .DistinctBy(line => (line.At, line.Text))
            .OrderBy(line => line.At)
            .ToArray();
        for (var i = 0; i < sorted.Length; i++)
        {
            var line = sorted[i];
            var boundary = i + 1 < sorted.Length ? sorted[i + 1].At : duration;
            var words = line.Words.ToArray();
            if (words.Length > 0 && words[^1].End == words[^1].Start)
            {
                // Enhanced LRC may omit a terminal timestamp. Use a real next-line/track
                // boundary only; without one, retain honest line synchronization.
                if (boundary is not { } end || end <= words[^1].Start)
                    words = Array.Empty<TimedWord>();
                else
                    words[^1] = words[^1] with { End = end };
            }
            if (boundary is { } limit && words.Any(w => w.End > limit))
                words = Array.Empty<TimedWord>();
            sorted[i] = line with { End = boundary, Words = words };
        }
        return sorted;
    }

    private static TimeSpan ReadTime(Match match) => TimeSpan.FromSeconds(
        int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture) * 60 +
        double.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture));
}
