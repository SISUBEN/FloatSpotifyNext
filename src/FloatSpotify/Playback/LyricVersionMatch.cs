using System.Text.RegularExpressions;

namespace FloatSpotify.Playback;

internal static class LyricVersionMatch
{
    private static readonly Regex Version = new(
        @"(?<!\p{L})(?<language>English|Chinese|Mandarin|Cantonese|Japanese|Korean|EN|ZH|JP|JA|KO)\s*ver(?:sion)?\.?(?!\p{L})|(?<language>中文|国语|國語|粤语|粵語|英文|英语|英語|日语|日語|韩语|韓語)版",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Instrumental = new(
        @"伴奏|纯音乐|純音樂|instrumental|accompaniment|karaoke|off[\s-]?vocal|backing[\s-]?track",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string? Language(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var match = Version.Match(title);
        if (!match.Success) return null;
        return match.Groups["language"].Value.ToLowerInvariant() switch
        {
            "english" or "en" or "英文" or "英语" or "英語" => "en",
            "chinese" or "mandarin" or "cantonese" or "zh" or "中文" or "国语" or "國語" or "粤语" or "粵語" => "zh",
            "japanese" or "jp" or "ja" or "日语" or "日語" => "ja",
            "korean" or "ko" or "韩语" or "韓語" => "ko",
            _ => null
        };
    }

    public static bool CandidateMatches(string requested, string? returned)
    {
        if (Instrumental.IsMatch(requested) != Instrumental.IsMatch(returned ?? string.Empty))
            return false;
        var expected = Language(requested);
        var candidate = Language(returned);
        return expected is null || candidate is null || expected == candidate;
    }

    public static bool TextMatches(string track, IReadOnlyList<TimedLyric> lyrics)
    {
        var language = Language(track);
        if (language is null) return true;
        var text = string.Concat(lyrics.Select(line => line.Text));
        var han = text.Count(c => c is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff');
        var kana = text.Count(c => c is >= '\u3040' and <= '\u30ff');
        var hangul = text.Count(c => c is >= '\uac00' and <= '\ud7af' or >= '\u1100' and <= '\u11ff');
        var latin = text.Count(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        // Conservative script consistency, not general language identification.
        // Allow mixed lyrics, but never substitute all-English text for a Chinese version.
        return language switch
        {
            "zh" => han > 0 && han > kana + hangul,
            "ja" => kana > 0,
            "ko" => hangul > 0,
            "en" => latin > 0 && latin > han + kana + hangul,
            _ => true
        };
    }
}
