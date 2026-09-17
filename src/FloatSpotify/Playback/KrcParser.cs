using System.Globalization;
using System.Text.RegularExpressions;

namespace FloatSpotify.Playback;

/// <summary>
/// 酷狗 KRC 歌词解析（输入是已解密解压的明文）。
/// <para>
/// 格式：<c>[行起点ms,行时长ms]&lt;词偏移ms,词时长ms,未用&gt;词文本...</c>。
/// 词偏移是**相对本行起点**的毫秒数，所以词的绝对时间是
/// <c>行起点 + 词偏移</c> 到 <c>行起点 + 词偏移 + 词时长</c>。
/// </para>
/// <para>
/// 2026-09 实测 10 首歌共 425 行 / 3800 个词，确认了三条必须遵守的事实：
/// <list type="bullet">
/// <item>存在**时长为 0** 的词（286 个），也存在**相邻词起点相同**的行（75 行）
///   → 校验只能要求「不倒退」，不能要求严格递增；</item>
/// <item>「末词结束 == 行尾」并不总成立（425 行里有 74 行不等），有 22 行甚至让词越出了行区间
///   → **不能**拿行时长去卡词的边界，否则会误杀这些行的逐字歌词；</item>
/// <item>词偏移从不倒退（0 行违规）→ 这条可以放心当作校验。</item>
/// </list>
/// </para>
/// <para>
/// 任何一项校验不过就丢掉逐字信息、只保留行级 —— 宁可少一个效果，也不能让高亮对错位置。
/// </para>
/// </summary>
internal static class KrcParser
{
    private static readonly Regex LinePattern = new(
        @"^\[(?<start>\d+),(?<duration>\d+)\](?<body>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WordPattern = new(
        @"<(?<offset>\d+),(?<duration>\d+),\d+>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OffsetPattern = new(
        @"\[\s*offset\s*:\s*(?<milliseconds>[+-]?\d+)\s*\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// 解析 KRC 文本。无法解析或没有可用行时返回空集合。
    /// </summary>
    /// <param name="stripCredits">
    /// 是否丢弃制作人员署名行。酷狗和网易云一样会把「作词 : 某某」带上时间戳塞进歌词，
    /// 所以调用方传 true。
    /// </param>
    public static IReadOnlyList<TimedLyric> Parse(string? krc, bool stripCredits = false, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(krc))
            return Array.Empty<TimedLyric>();

        var sourceLines = krc.Split(
            ["\r\n", "\n", "\r"],
            StringSplitOptions.None);

        // KRC 也允许用一行 [offset:±ms] 整体平移时间轴。
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
        foreach (var sourceLine in sourceLines)
        {
            var match = LinePattern.Match(sourceLine);
            if (!match.Success)
                continue;
            if (!long.TryParse(
                    match.Groups["start"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var startMilliseconds) ||
                // 行时长本身不参与计算：实测有 22/425 行的词会越出它，拿它卡边界会误杀逐字歌词。
                // 这里只借 TryParse 顺带挡掉溢出的畸形值。
                !long.TryParse(
                    match.Groups["duration"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                continue;
            }

            var body = match.Groups["body"].Value;
            var text = WordPattern.Replace(body, string.Empty);
            if (stripCredits && LrcParser.IsCreditLine(text))
                continue;

            var at = TimeSpan.FromMilliseconds(startMilliseconds) + offset;
            if (at < TimeSpan.Zero)
                at = TimeSpan.Zero;

            result.Add(new TimedLyric(at, text) { Words = ParseWords(body, at) });
        }

        var sorted = result
            .DistinctBy(line => (line.At, line.Text))
            .OrderBy(line => line.At)
            .ToArray();
        for (var index = 0; index < sorted.Length; index++)
        {
            var line = sorted[index];
            var boundary = index + 1 < sorted.Length ? sorted[index + 1].At : duration;
            var words = line.Words.ToArray();
            // 逐字时间越过下一行起点，说明这份 KRC 的时间轴不可信，整行退回行级。
            if (boundary is { } limit && words.Any(word => word.End > limit))
                words = Array.Empty<TimedWord>();
            sorted[index] = line with { End = boundary, Words = words };
        }

        return sorted;
    }

    private static IReadOnlyList<TimedWord> ParseWords(string body, TimeSpan at)
    {
        var matches = WordPattern.Matches(body);
        if (matches.Count == 0)
            return Array.Empty<TimedWord>();

        // 正文前面还有没被词标签覆盖的文字时，逐字时间必然对不齐，整行退回行级。
        if (matches[0].Index != 0)
            return Array.Empty<TimedWord>();

        var words = new List<TimedWord>(matches.Count);
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            if (!long.TryParse(
                    match.Groups["offset"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var wordOffsetMilliseconds) ||
                !long.TryParse(
                    match.Groups["duration"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var wordMilliseconds))
            {
                return Array.Empty<TimedWord>();
            }

            var start = at + TimeSpan.FromMilliseconds(wordOffsetMilliseconds);
            var end = start + TimeSpan.FromMilliseconds(wordMilliseconds);
            var endIndex = index + 1 < matches.Count ? matches[index + 1].Index : body.Length;
            var wordText = body[(match.Index + match.Length)..endIndex];
            if (wordText.Length > 0)
                words.Add(new TimedWord(start, end, wordText));
        }

        if (words.Count == 0)
            return Array.Empty<TimedWord>();

        // 时长为 0 的词是真实存在的，所以这里只能要求「结束不早于开始、不早于行起点」。
        if (words.Any(word => word.End < word.Start || word.Start < at))
            return Array.Empty<TimedWord>();
        if (words.Zip(words.Skip(1)).Any(pair => pair.First.Start > pair.Second.Start))
            return Array.Empty<TimedWord>();

        return words;
    }
}
