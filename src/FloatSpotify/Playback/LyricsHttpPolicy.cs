using System.Net;
using System.Net.Http;

namespace FloatSpotify.Playback;

internal sealed class LyricsUnavailableException(TimeSpan retryAfter) : HttpRequestException
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

internal static class LyricsHttpPolicy
{
    public static void Check(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
        {
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(30);
            throw new LyricsUnavailableException(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1));
        }
    }
}
