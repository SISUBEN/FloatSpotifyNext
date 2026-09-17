using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FloatSpotify.Playback;

/// <summary>
/// 酷狗音乐歌词源，返回 **KRC 逐字**歌词。
/// <para>
/// ⚠️ 用的是 kugou.com 的<b>非公开</b> web/mobile 接口，不是官方开放平台。
/// 与网易云同理：随时可能变更、限流或失效，也存在 ToS 风险，因此
/// 默认关闭，必须由用户在设置里主动开启；任何失败都只是「取不到歌词」，绝不影响播放。
/// </para>
/// <para>
/// 流程（三步，与 github.com/bingaha/kugou-lrc 一致，2026-09 实测可用）：
/// <list type="number">
/// <item><c>GET mobileservice.kugou.com/api/v3/lyric/search?keyword=</c> → 候选歌曲的
///   <c>filename</c>（"歌手 - 歌名"）与 <c>hash</c>；<b>响应外面裹着一层 HTML 注释</b>，
///   需要按花括号截取；</item>
/// <item><c>GET krcs.kugou.com/search?hash=</c> → 该曲的歌词文件列表
///   <c>candidates[]</c>，含 <c>id</c> / <c>accesskey</c> / <c>song</c> / <c>singer</c> / <c>duration</c>（毫秒）；</item>
/// <item><c>GET lyrics.kugou.com/download?fmt=krc</c> → <c>content</c> 是 base64，
///   解开后前 4 字节是魔数 <c>krc1</c>，其余是「16 字节循环异或 + zlib 压缩」的明文 KRC。</item>
/// </list>
/// </para>
/// <para>
/// 搜索关键词**只用歌名**。实测加进歌手名反而会把官方版挤出结果：搜「晴天」第一条就是
/// <c>周杰伦 - 晴天</c>，搜「晴天 周杰伦」前 20 条全是翻唱。同名曲靠候选打分区分。
/// </para>
/// </summary>
internal sealed class KugouLyricsProvider : ILyricsProvider
{
    private const int SearchLimit = 20;

    /// <summary>
    /// KRC 的固定解密密钥，与官方客户端一致（JS/Python 实现里是同一个字节数组）。
    /// </summary>
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

    /// <summary>
    /// 第一步：按歌名搜索，挑出最匹配的一首歌的 hash；没有够格的就返回 null。
    /// </summary>
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

        // 响应形如 "<!--KG_TAG_RES_START-->{...}"，按花括号截取比按注释标记替换更耐用。
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

            // info[].duration 的单位是**秒**（candidates[].duration 才是毫秒）。
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

    /// <summary>
    /// 第二步：用 hash 查歌词文件列表，挑出最匹配的一份。
    /// </summary>
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

    /// <summary>
    /// 第三步：下载并解码。KRC 解不开时退回把内容当明文 LRC 解析，
    /// 这样「这首歌只有普通歌词」也不会整首没歌词。
    /// </summary>
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

        // 以魔数判断而不是相信 fmt 字段：fmt 声明 krc 但实际返回明文 LRC 的情况是存在的。
        if (bytes.Length <= 4 || !IsKrcPayload(bytes))
            return LrcParser.Parse(Encoding.UTF8.GetString(bytes), stripCredits: true, duration: null);

        var krcText = await InflateAsync(bytes, cancellationToken);
        if (krcText is null)
            return LrcParser.Parse(Encoding.UTF8.GetString(bytes), stripCredits: true, duration: null);

        return KrcParser.Parse(krcText, stripCredits: true);
    }

    private static bool IsKrcPayload(byte[] bytes) =>
        bytes[0] == (byte)'k' && bytes[1] == (byte)'r' && bytes[2] == (byte)'c' && bytes[3] == (byte)'1';

    /// <summary>
    /// 跳过 4 字节魔数 → 16 字节循环异或 → zlib 解压 → UTF-8。
    /// 解不开返回 null，由调用方决定降级策略。
    /// </summary>
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

    /// <summary>
    /// 候选歌曲的 <c>filename</c> 形如 "歌手 - 歌名"，但分隔符个数**不固定** ——
    /// 实测存在 "hjt - 晴天 - 周杰伦 - hjt" 这种（上传者 / 歌名 / 歌手 / 上传者）。
    /// 也就是说，正确的那一段既不在固定位置上，也不一定和歌手相邻。
    /// <para>
    /// 所以这里把每一段都当一次歌名、每一段都当一次歌手，两两试一遍取最高分。
    /// 段落数只有个位数，穷举的开销可以忽略；而 <see cref="LyricsMatchScore"/> 对曲名是硬门槛
    /// （对不上直接判 0），所以「多试几次」不会引入错配，只会多认出本来就能认出的那种。
    /// </para>
    /// </summary>
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
