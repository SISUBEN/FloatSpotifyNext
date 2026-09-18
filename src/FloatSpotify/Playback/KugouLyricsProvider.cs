using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FloatSpotify.Playback;

internal sealed class KugouLyricsProvider : ILyricsProvider
{
    private const int SearchLimit = 20;

    private static readonly byte[] XorKey =
    [
        0x40, 0x47, 0x61, 0x77, 0x5E, 0x32, 0x74, 0x47,
        0x51, 0x36, 0x31, 0x2D, 0xCE, 0xD2, 0x6E, 0x69
    ];

    private readonly HttpClient _httpClient;
    private readonly LyricsCache _cache;

    public KugouLyricsProvider(HttpClient httpClient, LyricsCache? cache = null)
    {
        _httpClient = httpClient;
        _cache = cache ?? new LyricsCache();
    }

    public LyricsSource Source => LyricsSource.Kugou;

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

        var hash = await FindHashAsync(track, artist, duration, cancellationToken);
        if (hash is null)
            return Array.Empty<TimedLyric>();

        var candidate = await FindCandidateAsync(hash, track, artist, duration, cancellationToken);
        if (candidate is null)
            return Array.Empty<TimedLyric>();

        var lyrics = await DownloadAsync(candidate.Value.Id, candidate.Value.AccessKey, cancellationToken);
        if (!LyricVersionMatch.TextMatches(track, lyrics) ||
            duration > TimeSpan.Zero && lyrics.Any(line => line.At > duration + TimeSpan.FromSeconds(2)))
        {
            return Array.Empty<TimedLyric>();
        }

        if (lyrics.Count > 0)
            await _cache.WriteAsync(Source, track, artist, lyrics, cancellationToken, duration);

        return lyrics;
    }

    private async Task<string?> FindHashAsync(
        string track,
        string artist,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var uri = "https://mobileservice.kugou.com/api/v3/lyric/search" +
                  $"?version=9108&highlight=1&keyword={Uri.EscapeDataString(track)}" +
                  $"&plat=0&pagesize={SearchLimit}&area_code=1&page=1&with_res_tag=1";

        var text = await GetTextAsync(uri, cancellationToken);
        if (text is null)
            return null;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        using var document = ParseJson(text[start..(end + 1)]);
        if (document is null ||
            !document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("info", out var info) ||
            info.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? bestHash = null;
        var bestScore = 0;
        foreach (var item in info.EnumerateArray())
        {
            var filename = item.TryGetProperty("filename", out var filenameElement)
                ? filenameElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(filename))
                continue;

            var seconds = item.TryGetProperty("duration", out var durationElement) &&
                          durationElement.ValueKind == JsonValueKind.Number
                ? durationElement.GetDouble()
                : 0;

            var score = ScoreFilename(filename, seconds, track, artist, duration);
            if (score <= bestScore)
                continue;

            var hash = ReadHash(item);
            if (hash is null)
                continue;

            bestScore = score;
            bestHash = hash;
        }

        return bestScore >= LyricsMatchScore.Minimum ? bestHash : null;
    }

    private async Task<KugouCandidate?> FindCandidateAsync(
        string hash,
        string track,
        string artist,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var uri = "https://krcs.kugou.com/search?ver=1&man=yes&client=mobi" +
                  $"&keyword=&duration=&hash={Uri.EscapeDataString(hash)}&album_audio_id=";

        var text = await GetTextAsync(uri, cancellationToken);
        if (text is null)
            return null;

        using var document = ParseJson(text);
        if (document is null ||
            !document.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        KugouCandidate? best = null;
        var bestScore = 0;
        foreach (var candidate in candidates.EnumerateArray())
        {
            var song = candidate.TryGetProperty("song", out var songElement) ? songElement.GetString() : null;
            var singer = candidate.TryGetProperty("singer", out var singerElement) ? singerElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(song))
                continue;

            var seconds = candidate.TryGetProperty("duration", out var durationElement) &&
                          durationElement.ValueKind == JsonValueKind.Number
                ? durationElement.GetDouble() / 1000d
                : 0;

            var score = LyricsMatchScore.Score(song, singer ?? string.Empty, seconds, track, artist, duration);
            if (score <= bestScore)
                continue;

            if (!candidate.TryGetProperty("id", out var idElement) ||
                !candidate.TryGetProperty("accesskey", out var keyElement))
            {
                continue;
            }

            var id = idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : idElement.GetRawText();
            var accessKey = keyElement.GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(accessKey))
                continue;

            bestScore = score;
            best = new KugouCandidate(id, accessKey);
        }

        return bestScore >= LyricsMatchScore.Minimum ? best : null;
    }

    private async Task<IReadOnlyList<TimedLyric>> DownloadAsync(
        string id,
        string accessKey,
        CancellationToken cancellationToken)
    {
        var uri = "https://lyrics.kugou.com/download?ver=1&client=pc" +
                  $"&id={Uri.EscapeDataString(id)}" +
                  $"&accesskey={Uri.EscapeDataString(accessKey)}" +
                  "&fmt=krc&charset=utf8";

        var text = await GetTextAsync(uri, cancellationToken);
        if (text is null)
            return Array.Empty<TimedLyric>();

        using var document = ParseJson(text);
        var content = document is not null &&
                      document.RootElement.TryGetProperty("content", out var contentElement) &&
                      contentElement.ValueKind == JsonValueKind.String
            ? contentElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<TimedLyric>();

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(content);
        }
        catch (FormatException)
        {
            return Array.Empty<TimedLyric>();
        }

        if (bytes.Length <= 4 || !IsKrcPayload(bytes))
            return LrcParser.Parse(Encoding.UTF8.GetString(bytes), stripCredits: true, duration: null);

        var krcText = await InflateAsync(bytes, cancellationToken);
        if (krcText is null)
            return LrcParser.Parse(Encoding.UTF8.GetString(bytes), stripCredits: true, duration: null);

        return KrcParser.Parse(krcText, stripCredits: true);
    }

    private static bool IsKrcPayload(byte[] bytes) =>
        bytes[0] == (byte)'k' && bytes[1] == (byte)'r' && bytes[2] == (byte)'c' && bytes[3] == (byte)'1';

    private static async Task<string?> InflateAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        var payload = new byte[bytes.Length - 4];
        Array.Copy(bytes, 4, payload, 0, payload.Length);
        for (var index = 0; index < payload.Length; index++)
            payload[index] ^= XorKey[index % XorKey.Length];

        try
        {
            using var compressed = new MemoryStream(payload);
            using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
            using var reader = new StreamReader(inflate, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    internal static int ScoreFilename(
        string filename,
        double seconds,
        string track,
        string artist,
        TimeSpan duration)
    {
        var parts = filename
            .Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return 0;
        if (parts.Length == 1)
            return LyricsMatchScore.Score(parts[0], string.Empty, seconds, track, artist, duration);

        var best = 0;
        for (var titleIndex = 0; titleIndex < parts.Length; titleIndex++)
        {
            for (var artistIndex = 0; artistIndex < parts.Length; artistIndex++)
            {
                if (artistIndex == titleIndex)
                    continue;

                var score = LyricsMatchScore.Score(
                    parts[titleIndex], parts[artistIndex], seconds, track, artist, duration);
                if (score > best)
                    best = score;
            }
        }

        return best;
    }

    private static string? ReadHash(JsonElement item)
    {
        foreach (var name in new[] { "hash", "320hash" })
        {
            if (item.TryGetProperty(name, out var element) &&
                element.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(element.GetString()))
            {
                return element.GetString();
            }
        }

        return null;
    }

    private async Task<string?> GetTextAsync(string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            LyricsHttpPolicy.Check(response);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (LyricsUnavailableException)
        {
            throw; // Let the coordinator honor Retry-After across tracks.
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static JsonDocument? ParseJson(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private readonly record struct KugouCandidate(string Id, string AccessKey);
}
