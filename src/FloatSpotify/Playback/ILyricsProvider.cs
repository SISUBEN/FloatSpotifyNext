namespace FloatSpotify.Playback;

/// <summary>
/// 单个歌词源的取词接口。实现类负责成功缓存；未命中返回空，网络及解析异常由协调器隔离。
/// </summary>
internal interface ILyricsProvider
{
    LyricsSource Source { get; }

    Task<IReadOnlyList<TimedLyric>> GetAsync(
        string track,
        string artist,
        TimeSpan duration,
        CancellationToken cancellationToken);
}
