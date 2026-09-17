using System.Net.Http;
using System.Text.Json;

namespace FloatSpotify.Playback;

/// <summary>Optional word-timing sources using the existing provider seam.</summary>
internal sealed class WordLyricsProvider(HttpClient httpClient, LyricsSource source, LyricsCache? cache = null) : ILyricsProvider
{
    private readonly LyricsCache _cache = cache ?? new();
    public LyricsSource Source => source;

    public async Task<IReadOnlyList<TimedLyric>> GetAsync(string track, string artist,
        TimeSpan duration, CancellationToken cancellationToken)
    {
        var cached = await _cache.TryReadAsync(Source, track, artist, cancellationToken, duration);
        if (cached is not null) return cached;
        var seconds = Math.Max(0, (int)Math.Round(duration.TotalSeconds));
        var uri = source == LyricsSource.Karalyr
            ? $"https://karalyr.com/api/get?track_name={Uri.EscapeDataString(track)}&artist_name={Uri.EscapeDataString(artist)}&duration={seconds}"
            : $"https://lyrics-api.boidu.dev/getLyrics?s={Uri.EscapeDataString(track)}&a={Uri.EscapeDataString(artist)}&d={seconds}";
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        LyricsHttpPolicy.Check(response);
        if (!response.IsSuccessStatusCode) return Array.Empty<TimedLyric>();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        if (root.TryGetProperty("trackName", out var returnedTitle) && returnedTitle.ValueKind == JsonValueKind.String &&
            !LyricVersionMatch.CandidateMatches(track, returnedTitle.GetString())) return Array.Empty<TimedLyric>();
        IReadOnlyList<TimedLyric> lyrics;
        if (source == LyricsSource.Karalyr)
        {
            lyrics = root.TryGetProperty("syncedLyrics", out var lrc) && lrc.ValueKind == JsonValueKind.String
                ? LrcParser.Parse(lrc.GetString(), duration: duration) : Array.Empty<TimedLyric>();
        }
        else
        {
            // Reject known low-confidence matches rather than displaying a wrong recording.
            if (root.TryGetProperty("score", out var score) &&
                (!score.TryGetDouble(out var confidence) || confidence < 80))
                return Array.Empty<TimedLyric>();
            lyrics = root.TryGetProperty("ttml", out var ttml) && ttml.ValueKind == JsonValueKind.String
                ? TtmlParser.Parse(ttml.GetString()!) : Array.Empty<TimedLyric>();
        }
        if (!LyricVersionMatch.TextMatches(track, lyrics) ||
            duration > TimeSpan.Zero && lyrics.Any(line => line.At > duration + TimeSpan.FromSeconds(2)))
            return Array.Empty<TimedLyric>();
        if (lyrics.Count > 0) await _cache.WriteAsync(Source, track, artist, lyrics, cancellationToken, duration);
        return lyrics;
    }
}
