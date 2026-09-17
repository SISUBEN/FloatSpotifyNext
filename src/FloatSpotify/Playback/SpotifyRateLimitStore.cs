using System.IO;
using System.Text.Json;

namespace FloatSpotify.Playback;

internal sealed record SpotifyRateLimitState(
    string ClientId,
    DateTimeOffset RetryAt);

internal sealed class SpotifyRateLimitStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _statePath;

    public SpotifyRateLimitStore(string? statePath = null)
    {
        if (!string.IsNullOrWhiteSpace(statePath))
        {
            _statePath = statePath;
            return;
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FloatSpotify.Next");
        _statePath = Path.Combine(directory, "spotify-rate-limit.json");
    }

    public DateTimeOffset Load(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return DateTimeOffset.MinValue;

        try
        {
            if (!File.Exists(_statePath))
                return DateTimeOffset.MinValue;

            var state = JsonSerializer.Deserialize<SpotifyRateLimitState>(
                File.ReadAllText(_statePath),
                JsonOptions);
            if (state is null ||
                !string.Equals(state.ClientId, clientId, StringComparison.Ordinal) ||
                state.RetryAt <= DateTimeOffset.UtcNow)
            {
                return DateTimeOffset.MinValue;
            }

            return state.RetryAt;
        }
        catch (JsonException)
        {
            return DateTimeOffset.MinValue;
        }
        catch (IOException)
        {
            return DateTimeOffset.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    public void Save(string? clientId, DateTimeOffset retryAt)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return;

        try
        {
            var directory = Path.GetDirectoryName(_statePath)!;
            Directory.CreateDirectory(directory);

            var temporaryPath = _statePath + ".tmp";
            var state = new SpotifyRateLimitState(clientId, retryAt);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporaryPath, _statePath, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
