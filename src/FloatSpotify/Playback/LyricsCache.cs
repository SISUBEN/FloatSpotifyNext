using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FloatSpotify.Playback;

internal sealed class LyricsCache
{
    private const string CacheVersion = "v5";

    private readonly string _cacheDirectory;

    public LyricsCache(string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FloatSpotify.Next",
            "lyrics");
    }

    public async Task<IReadOnlyList<TimedLyric>?> TryReadAsync(
        LyricsSource source,
        string track,
        string artist,
        CancellationToken cancellationToken,
        TimeSpan duration = default)
    {
        try
        {
            var path = PathFor(source, track, artist, duration);
            if (!File.Exists(path))
                return null;

            await using var stream = File.OpenRead(path);
            var entries = await JsonSerializer.DeserializeAsync<TimedLyric[]>(
                stream,
                cancellationToken: cancellationToken);

            if (entries is null || entries.Any(line => line is null || line.Text is null ||
                line.Words is null || line.Words.Any(w => w is null || w.Text is null || w.End < w.Start)))
                return null;
            var lifetime = entries.Any(line => line.Words.Count > 0) ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
            if (!LyricVersionMatch.TextMatches(track, entries)) return null;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > lifetime) return null;
            return entries;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task WriteAsync(
        LyricsSource source,
        string track,
        string artist,
        IReadOnlyList<TimedLyric> lyrics,
        CancellationToken cancellationToken,
        TimeSpan duration = default)
    {
        string? temporaryPath = null;
        if (!LyricVersionMatch.TextMatches(track, lyrics)) return;
        try
        {
            var path = PathFor(source, track, artist, duration);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            temporaryPath = path + $".{Guid.NewGuid():N}.tmp";

            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    lyrics,
                    cancellationToken: cancellationToken);
            }

            File.Move(temporaryPath, path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private string PathFor(LyricsSource source, string track, string artist, TimeSpan duration)
    {
        var key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{artist.Trim().ToUpperInvariant()}\n{track.Trim().ToUpperInvariant()}\n{Math.Round(duration.TotalSeconds)}")));
        return Path.Combine(_cacheDirectory, $"{CacheVersion}-{source}-{key}.json");
    }

}
