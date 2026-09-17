using System.Text;

namespace FloatSpotify.Playback;

/// <summary>
/// 「搜到的候选曲目」和「正在播放的曲目」之间的匹配打分。
/// <para>
/// 网易云和酷狗都是「关键词搜索 → 从候选里挑最匹配的一首 → 取词」的两段式，
/// 判定标准完全一致，所以共用这一份实现。
/// </para>
/// <para>
/// 原则：**宁可漏，不可错**。曲名对不上直接判 0 —— 给错歌的歌词比不给歌词更糟，
/// 用户很难发现。
/// </para>
/// </summary>
internal static class LyricsMatchScore
{
    /// <summary>最低匹配分。低于它宁可返回空。</summary>
    public const int Minimum = 55;

    /// <summary>时长差到这个程度基本可以判定是另一个版本（现场版 / 混音版），倒扣分。</summary>
    public const double VersionMismatchSeconds = 25;

    /// <summary>
    /// 给候选曲目打分。<paramref name="candidateDurationSeconds"/> 未知时传 0，不做时长加减分。
    /// </summary>
    public static int Score(
        string candidateTitle,
        string candidateArtist,
        double candidateDurationSeconds,
        string track,
        string artist,
        TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(candidateTitle))
            return 0;
        if (!LyricVersionMatch.CandidateMatches(track, candidateTitle))
            return 0;

        var targetTitle = Normalize(track);
        var title = Normalize(candidateTitle);
        if (targetTitle.Length == 0 || title.Length == 0)
            return 0;

        int score;
        if (title == targetTitle)
            score = 50;
        else if (title.Contains(targetTitle) || targetTitle.Contains(title))
            score = 25;
        else
            return 0;

        var targetArtist = Normalize(artist);
        var candidateArtistNormalized = Normalize(candidateArtist);
        if (targetArtist.Length > 0 && candidateArtistNormalized.Length > 0)
        {
            if (candidateArtistNormalized == targetArtist)
                score += 30;
            else if (candidateArtistNormalized.Contains(targetArtist) || targetArtist.Contains(candidateArtistNormalized))
                score += 15;
        }

        // 时长未知时（部分播放源读不到）不做加减分，避免误杀。
        if (duration > TimeSpan.Zero && candidateDurationSeconds > 0)
        {
            var difference = Math.Abs(candidateDurationSeconds - duration.TotalSeconds);
            if (difference <= 3)
                score += 20;
            else if (difference <= 10)
                score += 10;
            else if (difference > VersionMismatchSeconds)
                score -= 30;
        }

        return score;
    }

    /// <summary>
    /// 归一化：只保留字母与数字并转小写。这样 "Shape of You"、"shape of you"、
    /// "Shape Of You (Live)" 的公共部分都能对上，同时天然丢掉空格和括号。
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
