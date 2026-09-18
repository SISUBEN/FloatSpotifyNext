namespace FloatSpotify.Playback;

internal static class PlainLyricsParser
{
    public static IReadOnlyList<TimedLyric> Parse(string? text, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<TimedLyric>();

        var lines = text
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        if (lines.Length < 2 || duration <= TimeSpan.Zero)
            return new[] { new TimedLyric(TimeSpan.Zero, text.Trim()) { IsPlainText = true } };

        var result = new TimedLyric[lines.Length];
        for (var index = 0; index < lines.Length; index++)
        {
            var at = TimeSpan.FromTicks(duration.Ticks * index / lines.Length);
            var end = TimeSpan.FromTicks(duration.Ticks * (index + 1) / lines.Length);
            result[index] = new TimedLyric(at, lines[index])
            {
                End = end,
                IsEstimatedTiming = true
            };
        }

        return result;
    }
}
