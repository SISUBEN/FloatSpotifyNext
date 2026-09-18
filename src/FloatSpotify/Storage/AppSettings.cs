using FloatSpotify.Localization;
using FloatSpotify.Playback;
using System.Text.Json.Serialization;

namespace FloatSpotify.Storage;

public sealed class AppSettings
{
    public PlaybackSource PlaybackSource { get; set; } = PlaybackSource.Spotify;

    public AppLanguage? Language { get; set; }

    [JsonIgnore]
    public string SpotifyClientId { get; set; } = string.Empty;
    public double FontSize { get; set; } = 46;
    public string FontFamilyName { get; set; } = "Segoe UI Variable Display";
    public double OverlayWidth { get; set; } = 800;
    public OverlayPlacement OverlayPlacement { get; set; } = OverlayPlacement.Custom;
    public double Opacity { get; set; } = 0.96;
    public string TextColor { get; set; } = "#FFFFFFFF";
    public string SecondaryTextColor { get; set; } = "#B8FFFFFF";
    public bool GlowEnabled { get; set; } = true;
    public bool ShowNextLine { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public bool IsLocked { get; set; }
    public double LyricOffsetSeconds { get; set; }
    public Dictionary<string, double> TrackLyricOffsets { get; set; } = new();
    public double? Left { get; set; }
    public double? Top { get; set; }

    public List<LyricsSourceOption> LyricsSources { get; set; } = DefaultLyricsSources();

    public static List<LyricsSourceOption> DefaultLyricsSources() =>
    [
        new LyricsSourceOption { Source = LyricsSource.Lrclib, Enabled = true },
        new LyricsSourceOption { Source = LyricsSource.NetEase, Enabled = false },
        new LyricsSourceOption { Source = LyricsSource.Kugou, Enabled = false },
        new LyricsSourceOption { Source = LyricsSource.Karalyr, Enabled = true },
        new LyricsSourceOption { Source = LyricsSource.BetterLyrics, Enabled = true }
    ];
}

public sealed class LyricsSourceOption
{
    public LyricsSource Source { get; set; }

    public bool Enabled { get; set; }
}
