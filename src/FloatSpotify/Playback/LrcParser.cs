using System.Globalization;
using System.Text.RegularExpressions;

namespace FloatSpotify.Playback;

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

    private static readonly Regex CreditPattern = new(
        @"^\s*(作词|作曲|编曲|作词人|作曲人|制作人|出品人|监制|统筹|企划|宣传|发行|出品|制作|制谱|" +
        @"人声录音棚|人声录音师|乐器录音棚|乐器录音师|录音|录音师|录音室|混音|混音师|母带|母带制作|母带工程师|母带处理|" +
        @"电吉他|电贝司|架子鼓|吉他|贝斯|鼓|键盘|弦乐|和声|配唱|人声|美术|设计|" +
        @"词曲|词|曲|OP|SP)(?:\s+[A-Za-z][A-Za-z ]{0,40})?\s*[:：]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool IsCreditLine(string? text) =>
        !string.IsNullOrWhiteSpace(text) && CreditPattern.IsMatch(text);

    public static IReadOnlyList<TimedLyric> Parse(string? lrc, bool stripCredits = false, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(lrc))
            return Array.Empty<TimedLyric>();

        var sourceLines = lrc.Split(
            ["\r\n", "\n", "\r"],
            StringSplitOptions.None);

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
