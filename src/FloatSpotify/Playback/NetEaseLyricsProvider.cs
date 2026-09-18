using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FloatSpotify.Playback;

internal sealed class NetEaseLyricsProvider : ILyricsProvider
{
    private const int SearchLimit = 10;

    private readonly HttpClient _httpClient;
    private readonly LyricsCache _cache = new();

    public NetEaseLyricsProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public LyricsSource Source => LyricsSource.NetEase;

    public async Task<IReadOnlyList<TimedLyric>> GetAsync(
        string track,
        string artist,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(track))
            return Array.Empty<TimedLyric>();

        var cached = await _cache.TryReadAsync(Source, track, artist, cancellationToken, duration);
        if (cached is not null)
            return cached;

        var songId = await FindSongIdAsync(track, artist, duration, cancellationToken);
        if (songId is null)
            return Array.Empty<TimedLyric>();

        var lyrics = await FetchLyricsAsync(songId.Value, cancellationToken);
        if (!LyricVersionMatch.TextMatches(track, lyrics)) return Array.Empty<TimedLyric>();
        if (lyrics.Count > 0)
            await _cache.WriteAsync(Source, track, artist, lyrics, cancellationToken, duration);

        return lyrics;
    }

    private async Task<long?> FindSongIdAsync(
        string track,
        string artist,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(artist) ? track : $"{track} {artist}";
        var uri = "https://music.163.com/api/search/get/web" +
                  $"?s={Uri.EscapeDataString(query)}" +
                  $"&type=1&offset=0&limit={SearchLimit}";

        using var document = await GetJsonAsync(uri, cancellationToken);
        if (document is null)
            return null;

        if (!document.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("songs", out var songs) ||
            songs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        long? bestId = null;
        var bestScore = 0;
        foreach (var song in songs.EnumerateArray())
        {
            var score = ScoreCandidate(song, track, artist, duration);
            if (score <= bestScore)
                continue;
            if (!song.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            bestScore = score;
            bestId = idElement.GetInt64();
        }

        return bestScore >= LyricsMatchScore.Minimum ? bestId : null;
    }

    private async Task<IReadOnlyList<TimedLyric>> FetchLyricsAsync(
        long songId,
        CancellationToken cancellationToken)
    {
        var uri = "https://music.163.com/api/song/lyric" +
                  $"?id={songId}&lv=-1&kv=-1&tv=-1";

        using var document = await GetJsonAsync(uri, cancellationToken);
        if (document is null)
            return Array.Empty<TimedLyric>();

        var root = document.RootElement;

        if (root.TryGetProperty("nolyric", out var noLyric) && noLyric.ValueKind == JsonValueKind.True)
            return Array.Empty<TimedLyric>();
        if (root.TryGetProperty("uncollected", out var uncollected) && uncollected.ValueKind == JsonValueKind.True)
            return Array.Empty<TimedLyric>();

        if (!root.TryGetProperty("lrc", out var lrc) ||
            !lrc.TryGetProperty("lyric", out var lyricElement))
        {
            return Array.Empty<TimedLyric>();
        }

        return LrcParser.Parse(lyricElement.GetString(), stripCredits: true);
    }

    private static int ScoreCandidate(
        JsonElement song,
        string track,
        string artist,
        TimeSpan duration)
    {
        var candidateName = song.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(candidateName))
            return 0;

        var seconds = song.TryGetProperty("duration", out var durationElement) &&
                      durationElement.ValueKind == JsonValueKind.Number
            ? durationElement.GetDouble() / 1000d
            : 0;

        return LyricsMatchScore.Score(
            candidateName, ReadArtistNames(song), seconds, track, artist, duration);
    }

    private static string ReadArtistNames(JsonElement song)
    {
        if (!song.TryGetProperty("artists", out var artists) ||
            artists.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var entry in artists.EnumerateArray())
        {
            if (entry.TryGetProperty("name", out var entryName) &&
                entryName.GetString() is { Length: > 0 } value)
            {
                builder.Append(value);
            }
        }

        return builder.ToString();
    }

    private async Task<JsonDocument?> GetJsonAsync(string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Referrer = new Uri("https://music.163.com/");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            LyricsHttpPolicy.Check(response);
            if (!response.IsSuccessStatusCode)
                return null;

            return JsonDocument.Parse(
                await response.Content.ReadAsStreamAsync(cancellationToken));
        }
        catch (LyricsUnavailableException)
        {
            throw; // Let the coordinator honor Retry-After across tracks.
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
