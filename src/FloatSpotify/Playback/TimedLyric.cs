namespace FloatSpotify.Playback;

/// <summary>
/// A line with optional provider-supplied word/syllable timing.
/// </summary>
public sealed record TimedLyric(TimeSpan At, string Text)
{
    public TimeSpan? End { get; init; }
    public bool IsPlainText { get; init; }
    public IReadOnlyList<TimedWord> Words { get; init; } = Array.Empty<TimedWord>();
}

public sealed record TimedWord(TimeSpan Start, TimeSpan End, string Text)
{
    public double Progress(TimeSpan position) => End <= Start
        ? (position >= Start ? 1 : 0)
        : Math.Clamp((position - Start).TotalSeconds / (End - Start).TotalSeconds, 0, 1);
}
