using System.IO;
using System.Text.Json;
using FloatSpotify.Playback;

namespace FloatSpotify.Storage;

public sealed class SettingsStore
{
    public const string SpotifyClientIdEnvironmentVariable =
        "FLOATSPOTIFY_SPOTIFY_CLIENT_ID";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    internal SettingsStore(string settingsPath) => _settingsPath = settingsPath;

    public SettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FloatSpotify.Next");

        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        var settings = new AppSettings();
        string? legacyClientId = null;

        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json) ?? settings;
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("SpotifyClientId", out var clientIdElement) &&
                    clientIdElement.ValueKind == JsonValueKind.String)
                {
                    legacyClientId = clientIdElement.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        var environmentClientId = ReadSpotifyClientIdEnvironmentVariable();
        if (!string.IsNullOrWhiteSpace(environmentClientId))
        {
            settings.SpotifyClientId = environmentClientId.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(legacyClientId))
        {
            settings.SpotifyClientId = legacyClientId.Trim();
            TrySaveSpotifyClientIdEnvironmentVariable(settings.SpotifyClientId);
        }

        if (legacyClientId is not null)
        {
            try
            {
                Save(settings);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        settings.TrackLyricOffsets ??= new Dictionary<string, double>();
        settings.LyricsSources = NormalizeLyricsSources(settings.LyricsSources);
        return settings;
    }

    internal static List<LyricsSourceOption> NormalizeLyricsSources(
        List<LyricsSourceOption>? configured)
    {
        var result = new List<LyricsSourceOption>();
        var seen = new HashSet<LyricsSource>();
        var allDisabled = configured is { Count: > 0 } && configured.All(option => !option.Enabled);

        foreach (var option in configured ?? new List<LyricsSourceOption>())
        {
            if (!Enum.IsDefined(option.Source) || !seen.Add(option.Source))
                continue;

            result.Add(option);
        }

        foreach (var fallback in AppSettings.DefaultLyricsSources())
        {
            if (seen.Add(fallback.Source))
                result.Add(new LyricsSourceOption { Source = fallback.Source, Enabled = fallback.Enabled && !allDisabled });
        }

        return result;
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _settingsPath, true);
    }

    public void SaveSpotifyClientIdEnvironmentVariable(string clientId)
    {
        var value = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
        Environment.SetEnvironmentVariable(
            SpotifyClientIdEnvironmentVariable,
            value,
            EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(
            SpotifyClientIdEnvironmentVariable,
            value,
            EnvironmentVariableTarget.User);
    }

    private static string? ReadSpotifyClientIdEnvironmentVariable()
    {
        return Environment.GetEnvironmentVariable(
                   SpotifyClientIdEnvironmentVariable,
                   EnvironmentVariableTarget.User)
               ?? Environment.GetEnvironmentVariable(SpotifyClientIdEnvironmentVariable);
    }

    private void TrySaveSpotifyClientIdEnvironmentVariable(string clientId)
    {
        try
        {
            SaveSpotifyClientIdEnvironmentVariable(clientId);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.Security.SecurityException)
        {
        }
    }
}
