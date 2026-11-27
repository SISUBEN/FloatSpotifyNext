namespace FloatSpotify.Playback;

/// <summary>
/// Aligns translated lines onto an original lyric timeline by timestamp.
/// </summary>
internal static class LyricTranslations
{
    // Sources disagree by a few hundred milliseconds; lines rarely start closer than this.
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1);

    // Below this share of aligned sung lines the donor is assumed to be another edit.
    private const double MinimumCoverage = 0.6;

    public static bool Any(IReadOnlyList<TimedLyric>? lyrics) =>
        lyrics is not null && lyrics.Any(line => !string.IsNullOrWhiteSpace(line.Translation));

    public static string? Normalize(string? translation, string original)
    {
        var text = translation?.Trim();
        if (string.IsNullOrEmpty(text) || text.Trim('/', ' ').Length == 0)
            return null;
        return string.Equals(text, original.Trim(), StringComparison.OrdinalIgnoreCase) ? null : text;
    }

    /// <summary>
    /// Copies the translation of the nearest donor line onto each target line. Returns null
    /// when the timelines do not line up well enough or nothing would be translated.
    /// </summary>
    public static IReadOnlyList<TimedLyric>? Attach(
        IReadOnlyList<TimedLyric> target,
        IReadOnlyList<TimedLyric> donor,
        Func<TimedLyric, string?> translationOf)
    {
        var anchors = donor
            .Where(line => !line.IsPlainText && !line.IsEstimatedTiming)
            .ToArray();
        if (anchors.Length == 0 || target.Any(line => line.IsPlainText || line.IsEstimatedTiming))
            return null;

        var result = new TimedLyric[target.Count];
        var sung = 0;
        var aligned = 0;
        var translated = 0;
        for (var index = 0; index < target.Count; index++)
        {
            var line = target[index];
            result[index] = line;
            if (string.IsNullOrWhiteSpace(line.Text))
                continue;

            sung++;
            TimedLyric? nearest = null;
            var bestDistance = TimeSpan.MaxValue;
            foreach (var anchor in anchors)
            {
                var distance = (anchor.At - line.At).Duration();
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = anchor;
                }
            }

            if (nearest is null || bestDistance > Tolerance)
                continue;

            aligned++;
            var translation = Normalize(translationOf(nearest), line.Text);
            if (translation is null)
                continue;

            translated++;
            result[index] = line with { Translation = translation };
        }

        return translated > 0 && aligned >= sung * MinimumCoverage ? result : null;
    }
}
