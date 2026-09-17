using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FloatSpotify.Playback;

/// <summary>
/// 网易云音乐歌词源。
/// <para>
/// ⚠️ 用的是 music.163.com 的 <b>非公开</b> web 接口，不是官方开放平台。
/// 这类接口随时可能变更、限流或失效，也存在 ToS 风险，因此：
/// 默认关闭，必须由用户在设置里主动开启；任何失败都只是「取不到歌词」，绝不影响播放。
/// </para>
/// <para>
/// 流程：搜索候选 → 打分挑最匹配的一首 → 取该曲的 LRC。
/// 接口与返回结构已实测确认（2026-09）：
/// <list type="bullet">
/// <item><c>GET /api/search/get/web?s=&amp;type=1&amp;limit=</c> → <c>result.songs[]</c>，
/// 含 <c>id</c> / <c>name</c> / <c>artists[].name</c> / <c>duration</c>（毫秒）</item>
/// <item><c>GET /api/song/lyric?id=&amp;lv=-1&amp;kv=-1&amp;tv=-1</c> → <c>lrc.lyric</c>，标准 LRC</item>
/// </list>
/// </para>
/// </summary>
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

    /// <summary>
    /// 搜索并挑出最匹配的一首，返回它的 id；没有够格的就返回 null。
    /// </summary>
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

        // 纯音乐 / 未收录：明确没有歌词，别再去解析。
        if (root.TryGetProperty("nolyric", out var noLyric) && noLyric.ValueKind == JsonValueKind.True)
            return Array.Empty<TimedLyric>();
        if (root.TryGetProperty("uncollected", out var uncollected) && uncollected.ValueKind == JsonValueKind.True)
            return Array.Empty<TimedLyric>();

        if (!root.TryGetProperty("lrc", out var lrc) ||
            !lrc.TryGetProperty("lyric", out var lyricElement))
        {
            return Array.Empty<TimedLyric>();
        }

        // stripCredits 传 true：网易云会把「作词 : 黄家驹」「混音 : …」这类署名行
        // 也带上时间戳塞进歌词里，不过滤会在开头显示成一堆曲目信息。
        return LrcParser.Parse(lyricElement.GetString(), stripCredits: true);
    }

    /// <summary>
    /// 给候选曲目打分。规则见 <see cref="LyricsMatchScore"/> —— 与酷狗源共用同一份判定，
    /// 避免「同一个道理在两个源里慢慢长歪」。
    /// </summary>
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

        // duration 的单位是毫秒；读不到时传 0，不做时长加减分。
        var seconds = song.TryGetProperty("duration", out var durationElement) &&
                      durationElement.ValueKind == JsonValueKind.Number
            ? durationElement.GetDouble() / 1000d
            : 0;

        return LyricsMatchScore.Score(
            candidateName, ReadArtistNames(song), seconds, track, artist, duration);
    }

    /// <summary>
    /// 把 <c>artists[]</c> 里的名字拼起来交给打分器（归一化由打分器统一负责）。
    /// </summary>
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
            // 网易云对缺少 Referer 的请求会拒绝或返回空数据。
            // User-Agent 不在这里设：两个引擎的 HttpClient 已经带了默认 UA，
            // 重复设置反而可能触发头部冲突。
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
            // 头部冲突之类的意外，按「这个源暂时不可用」处理。
            return null;
        }
    }
}
