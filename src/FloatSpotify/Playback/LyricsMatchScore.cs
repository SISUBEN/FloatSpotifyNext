using System.Text;

namespace FloatSpotify.Playback;

internal static class LyricsMatchScore
{
    public const int Minimum = 55;

    public const double VersionMismatchSeconds = 25;

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
