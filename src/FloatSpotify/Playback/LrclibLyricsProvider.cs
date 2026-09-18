using System.Net.Http;
using System.Text.Json;

namespace FloatSpotify.Playback;

internal sealed class LrclibLyricsProvider : ILyricsProvider
{
    private readonly HttpClient _httpClient;
    private readonly LyricsCache _cache;

    public LrclibLyricsProvider(HttpClient httpClient, LyricsCache? cache = null)
    {
        _httpClient = httpClient;
        _cache = cache ?? new();
    }

    public LyricsSource Source => LyricsSource.Lrclib;

    public async Task<IReadOnlyList<TimedLyric>> GetAsync(
        string track,
        string artist,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var cached = await _cache.TryReadAsync(Source, track, artist, cancellationToken, duration);
        if (cached is not null)
            return cached;

        var uri =
            "https://lrclib.net/api/get" +
            $"?track_name={Uri.EscapeDataString(track)}" +
            $"&artist_name={Uri.EscapeDataString(artist)}" +
            $"&duration={Math.Max(0, (int)Math.Round(duration.TotalSeconds))}";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        LyricsHttpPolicy.Check(response);
        if (!response.IsSuccessStatusCode)
            return Array.Empty<TimedLyric>();

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken));
        var syncedLyrics = document.RootElement.TryGetProperty("syncedLyrics", out var syncedElement)
            ? syncedElement.GetString()
            : null;
        if (document.RootElement.TryGetProperty("trackName", out var returnedTitle) && returnedTitle.ValueKind == JsonValueKind.String &&
            !LyricVersionMatch.CandidateMatches(track, returnedTitle.GetString())) return Array.Empty<TimedLyric>();

        var lyrics = LrcParser.Parse(syncedLyrics, duration: duration);
        if ((!lyrics.Any(line => !string.IsNullOrWhiteSpace(line.Text)) || !LyricVersionMatch.TextMatches(track, lyrics)) &&
            document.RootElement.TryGetProperty("plainLyrics", out var plain) && plain.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(plain.GetString()))
            lyrics = new[] { new TimedLyric(TimeSpan.Zero, plain.GetString()!) { IsPlainText = true } };
        if (!LyricVersionMatch.TextMatches(track, lyrics)) return Array.Empty<TimedLyric>();
        if (lyrics.Count > 0)
            await _cache.WriteAsync(Source, track, artist, lyrics, cancellationToken, duration);

        return lyrics;
    }
}
