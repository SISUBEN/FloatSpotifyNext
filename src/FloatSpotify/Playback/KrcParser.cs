using System.Globalization;
using System.Text.RegularExpressions;

namespace FloatSpotify.Playback;

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

    public static IReadOnlyList<TimedLyric> Parse(string? krc, bool stripCredits = false, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(krc))
            return Array.Empty<TimedLyric>();

        var sourceLines = krc.Split(
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

        if (words.Any(word => word.End < word.Start || word.Start < at))
            return Array.Empty<TimedWord>();
        if (words.Zip(words.Skip(1)).Any(pair => pair.First.Start > pair.Second.Start))
            return Array.Empty<TimedWord>();

        return words;
    }
}
