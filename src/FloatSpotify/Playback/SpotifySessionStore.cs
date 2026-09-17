using System.IO;
using System.Text.Json;

namespace FloatSpotify.Playback;

internal sealed record SpotifySession(
    string? ClientId,
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt);

internal sealed class SpotifySessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _sessionPath;

    public SpotifySessionStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FloatSpotify.Next");

        _sessionPath = Path.Combine(directory, "spotify-session.json");
    }

    public SpotifySession? Load(string clientId)
    {
        var session = ReadSession(_sessionPath);
        if (session is null)
            return null;

        if (string.IsNullOrWhiteSpace(session.ClientId))
        {
            return session with
            {
                ClientId = clientId,
                AccessToken = string.Empty,
                ExpiresAt = DateTimeOffset.MinValue
            };
        }

        return string.Equals(session.ClientId, clientId, StringComparison.Ordinal)
            ? session
            : null;
    }

    public void Save(SpotifySession session)
    {
        var directory = Path.GetDirectoryName(_sessionPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = _sessionPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(session, JsonOptions));
        File.Move(temporaryPath, _sessionPath, true);
    }

    public void Clear()
    {
        if (File.Exists(_sessionPath))
            File.Delete(_sessionPath);
    }

    private static SpotifySession? ReadSession(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var clientId = ReadString(root, "clientId");
            var accessToken = ReadString(root, "accessToken", "access_token");
            var refreshToken = ReadString(root, "refreshToken", "refresh_token");

            if (string.IsNullOrWhiteSpace(accessToken) && string.IsNullOrWhiteSpace(refreshToken))
                return null;

            var expiresAt = DateTimeOffset.MaxValue;
            if (root.TryGetProperty("expiresAt", out var expiresElement) &&
                expiresElement.TryGetDateTimeOffset(out var parsedExpiry))
            {
                expiresAt = parsedExpiry;
            }

            return new SpotifySession(clientId, accessToken ?? string.Empty, refreshToken, expiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }
}
