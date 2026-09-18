using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using FloatSpotify.Localization;
using FloatSpotify.Playback;
using FloatSpotify.Storage;

namespace FloatSpotify.ViewModels;

public sealed class OverlayViewModel : ObservableObject
{
    public const double MinimumOverlayWidth = 160;
    public const double MaximumOverlayWidth = 1600;

    private readonly PlaybackCoordinator _playbackEngine;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly LyricsCoordinator _lyricsCoordinator;

    private string _track = "FloatSpotify";
    private string _artist = "Rewrite preview";
    private string _currentLine = Loc.T("Overlay_Connecting");
    private string _nextLine = string.Empty;
    private string? _statusMessage;
    private bool _isPlaying = true;
    private bool _isSettingsOpen;
    private string? _currentLyricKey;
    public PlaybackFrame? LyricFrame { get; private set; } = new(
        "FloatSpotify", "", Loc.T("Overlay_Connecting"), "", false, TimeSpan.Zero, TimeSpan.Zero);

    public OverlayViewModel(
        PlaybackCoordinator playbackEngine,
        SettingsStore settingsStore,
        AppSettings settings,
        LyricsCoordinator lyricsCoordinator)
    {
        _playbackEngine = playbackEngine;
        _settingsStore = settingsStore;
        _settings = settings;
        _lyricsCoordinator = lyricsCoordinator;

        LyricsSources = new ObservableCollection<LyricsSourceItem>(
            _settings.LyricsSources.Select(
                option => new LyricsSourceItem(option.Source, option.Enabled, OnLyricsSourceToggled)));
        RefreshLyricsSourceMoveState();

        LanguageOptions =
        [
            new LanguageOption(LanguageChoice.System),
            new LanguageOption(LanguageChoice.ChineseSimplified),
            new LanguageOption(LanguageChoice.English)
        ];

        ApplyLyricsSourceConfiguration();
    }

    public event Action? ToggleControlsRequested;
    public event Action? SettingsRequested;
    public event Action? HideRequested;
    public event Action? ExitRequested;
    public event Action<OverlayPlacement>? PlacementRequested;

    public string Track
    {
        get => _track;
        private set
        {
            if (_track == value) return;
            _track = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string Artist
    {
        get => _artist;
        private set
        {
            if (_artist == value) return;
            _artist = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => string.IsNullOrWhiteSpace(_statusMessage)
        ? $"{Artist}  ·  {Track}"
        : _statusMessage;

    public string CurrentLine
    {
        get => _currentLine;
        private set
        {
            if (_currentLine == value) return;
            _currentLine = value;
            OnPropertyChanged();
        }
    }

    public string NextLine
    {
        get => _nextLine;
        private set
        {
            if (_nextLine == value) return;
            _nextLine = value;
            OnPropertyChanged();
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayPauseGlyph));
        }
    }

    public string PlayPauseGlyph => IsPlaying ? "⏸" : "▶";
    public string LockGlyph => IsLocked ? "🔒" : "🔓";

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (_isSettingsOpen == value) return;
            _isSettingsOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsSpotifySource
    {
        get => _settings.PlaybackSource == PlaybackSource.Spotify;
        set
        {
            if (value)
                SelectPlaybackSource(PlaybackSource.Spotify);
        }
    }

    public bool IsYouTubeMusicSource
    {
        get => _settings.PlaybackSource == PlaybackSource.YouTubeMusic;
        set
        {
            if (value)
                SelectPlaybackSource(PlaybackSource.YouTubeMusic);
        }
    }

    public LanguageChoice LanguageChoice
    {
        get => Loc.ToChoice(_settings.Language);
        set
        {
            if (Loc.ToChoice(_settings.Language) == value)
                return;

            _settings.Language = Loc.ToStored(value);
            Loc.Language = Loc.Resolve(value);
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public string SpotifyClientId
    {
        get => _settings.SpotifyClientId;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (_settings.SpotifyClientId == normalized) return;
            _settings.SpotifyClientId = normalized;
            try
            {
                _settingsStore.SaveSpotifyClientIdEnvironmentVariable(normalized);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (System.Security.SecurityException)
            {
            }
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public double FontSize
    {
        get => _settings.FontSize;
        set
        {
            var normalized = Math.Clamp(value, 22, 96);
            if (Math.Abs(_settings.FontSize - normalized) < 0.01) return;
            _settings.FontSize = normalized;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public string FontFamilyName
    {
        get => string.IsNullOrWhiteSpace(_settings.FontFamilyName)
            ? "Segoe UI Variable Display"
            : _settings.FontFamilyName;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? "Segoe UI Variable Display"
                : value.Trim();
            if (_settings.FontFamilyName == normalized) return;
            _settings.FontFamilyName = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ResolvedFontFamily));
            PersistSettings();
        }
    }

    public string ResolvedFontFamily => $"{FontFamilyName}, Global User Interface";

    public double OverlayWidth
    {
        get => Math.Clamp(
            _settings.OverlayWidth,
            MinimumOverlayWidth,
            MaximumOverlayWidth);
        set
        {
            var normalized = Math.Round(Math.Clamp(
                value,
                MinimumOverlayWidth,
                MaximumOverlayWidth));
            if (Math.Abs(_settings.OverlayWidth - normalized) < 0.01) return;
            _settings.OverlayWidth = normalized;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public OverlayPlacement OverlayPlacement
    {
        get => _settings.OverlayPlacement;
        set
        {
            if (_settings.OverlayPlacement == value) return;
            _settings.OverlayPlacement = value;
            OnPropertyChanged();
            PersistSettings();
            PlacementRequested?.Invoke(value);
        }
    }

    public double OverlayOpacity
    {
        get => _settings.Opacity;
        set
        {
            var normalized = Math.Clamp(value, 0.35, 1);
            if (Math.Abs(_settings.Opacity - normalized) < 0.001) return;
            _settings.Opacity = normalized;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public string TextColor
    {
        get => _settings.TextColor;
        set
        {
            if (_settings.TextColor == value) return;
            _settings.TextColor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TextRed));
            OnPropertyChanged(nameof(TextGreen));
            OnPropertyChanged(nameof(TextBlue));
            PersistSettings();
        }
    }

    public double TextRed
    {
        get => ReadTextRgb().Red;
        set => SetTextRgb(red: NormalizeColorChannel(value));
    }

    public double TextGreen
    {
        get => ReadTextRgb().Green;
        set => SetTextRgb(green: NormalizeColorChannel(value));
    }

    public double TextBlue
    {
        get => ReadTextRgb().Blue;
        set => SetTextRgb(blue: NormalizeColorChannel(value));
    }

    public string SecondaryTextColor
    {
        get => _settings.SecondaryTextColor;
        set
        {
            if (_settings.SecondaryTextColor == value) return;
            _settings.SecondaryTextColor = value;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public bool GlowEnabled
    {
        get => _settings.GlowEnabled;
        set
        {
            if (_settings.GlowEnabled == value) return;
            _settings.GlowEnabled = value;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public bool ShowNextLine
    {
        get => _settings.ShowNextLine;
        set
        {
            if (_settings.ShowNextLine == value) return;
            _settings.ShowNextLine = value;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public bool AlwaysOnTop
    {
        get => _settings.AlwaysOnTop;
        set
        {
            if (_settings.AlwaysOnTop == value) return;
            _settings.AlwaysOnTop = value;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public bool IsLocked
    {
        get => _settings.IsLocked;
        private set
        {
            if (_settings.IsLocked == value) return;
            _settings.IsLocked = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LockGlyph));
            PersistSettings();
        }
    }

    public double LyricOffsetSeconds
    {
        get
        {
            if (_currentLyricKey is not null &&
                _settings.TrackLyricOffsets.TryGetValue(_currentLyricKey, out var trackOffset))
                return trackOffset;

            return _settings.LyricOffsetSeconds;
        }
        set
        {
            var normalized = Math.Round(Math.Clamp(value, -5, 5), 1);
            if (Math.Abs(LyricOffsetSeconds - normalized) < 0.01) return;

            if (_currentLyricKey is null)
                _settings.LyricOffsetSeconds = normalized;
            else
                _settings.TrackLyricOffsets[_currentLyricKey] = normalized;

            OnPropertyChanged();
            PersistSettings();
            _ = _playbackEngine.ExecuteAsync(PlayerCommand.SetLyricOffset, normalized);
        }
    }

    public double? SavedLeft => _settings.Left;
    public double? SavedTop => _settings.Top;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _playbackEngine.ExecuteAsync(
            PlayerCommand.SetLyricOffset,
            LyricOffsetSeconds,
            cancellationToken);

        try
        {
            await foreach (var frame in _playbackEngine.WatchAsync(cancellationToken))
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ApplyFrame(frame));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public Task TogglePlaybackAsync() =>
        _playbackEngine.ExecuteAsync(PlayerCommand.TogglePlayback);

    public Task PreviousTrackAsync() =>
        _playbackEngine.ExecuteAsync(PlayerCommand.PreviousTrack);

    public Task NextTrackAsync() =>
        _playbackEngine.ExecuteAsync(PlayerCommand.NextTrack);

    public Task ReauthorizeSpotifyAsync() =>
        _playbackEngine.ExecuteAsync(PlayerCommand.Reauthorize);

    private void SelectPlaybackSource(PlaybackSource source)
    {
        if (_settings.PlaybackSource == source)
            return;

        _settings.PlaybackSource = source;
        _playbackEngine.SelectSource(source);
        OnPropertyChanged(nameof(IsSpotifySource));
        OnPropertyChanged(nameof(IsYouTubeMusicSource));
        PersistSettings();
    }

    public void ToggleLock()
    {
        IsLocked = !IsLocked;
        IsSettingsOpen = false;
    }

    public void Unlock()
    {
        IsLocked = false;
    }

    public void SetTextColor(string color)
    {
        var rgb = color.StartsWith('#') ? color[1..] : color;
        if (rgb.Length == 8)
            rgb = rgb[2..];
        if (rgb.Length != 6 ||
            !uint.TryParse(rgb, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            return;

        rgb = rgb.ToUpperInvariant();
        TextColor = $"#FF{rgb}";
        SecondaryTextColor = $"#B8{rgb}";
    }

    public void ResetLyricOffset()
    {
        if (_currentLyricKey is null)
            _settings.LyricOffsetSeconds = 0;
        else
            _settings.TrackLyricOffsets[_currentLyricKey] = 0;

        OnPropertyChanged(nameof(LyricOffsetSeconds));
        PersistSettings();
        _ = _playbackEngine.ExecuteAsync(PlayerCommand.SetLyricOffset, 0);
    }

    public void SavePosition(double left, double top, bool markAsCustom = true)
    {
        _settings.Left = left;
        _settings.Top = top;
        if (markAsCustom && _settings.OverlayPlacement != OverlayPlacement.Custom)
        {
            _settings.OverlayPlacement = OverlayPlacement.Custom;
            OnPropertyChanged(nameof(OverlayPlacement));
        }
        PersistSettings();
    }

    public void RequestToggleControls() => ToggleControlsRequested?.Invoke();
    public void RequestSettings() => SettingsRequested?.Invoke();
    public void RequestHide() => HideRequested?.Invoke();
    public void RequestExit() => ExitRequested?.Invoke();

    private void ApplyFrame(PlaybackFrame frame)
    {
        // Never allow an empty provider/status frame to remove the visible drag target.
        if (string.IsNullOrWhiteSpace(frame.CurrentLine))
            frame = frame with { CurrentLine = frame.ActiveLyric is not null ? "♪" :
                string.IsNullOrWhiteSpace(frame.Track) ? Loc.T("Overlay_NoLyrics_Hint") : $"{frame.Artist} · {frame.Track}",
                ActiveLyric = null };
        if (frame.Duration > TimeSpan.Zero)
        {
            var lyricKey = $"{frame.Artist}\n{frame.Track}";
            if (!string.Equals(_currentLyricKey, lyricKey, StringComparison.Ordinal))
            {
                _currentLyricKey = lyricKey;
                OnPropertyChanged(nameof(LyricOffsetSeconds));
                _ = _playbackEngine.ExecuteAsync(
                    PlayerCommand.SetLyricOffset,
                    LyricOffsetSeconds);
            }
        }

        Track = frame.Track;
        Artist = frame.Artist;
        CurrentLine = frame.CurrentLine;
        NextLine = frame.NextLine;
        IsPlaying = frame.IsPlaying;
        LyricFrame = frame;
        OnPropertyChanged(nameof(LyricFrame));
        if (_statusMessage != frame.StatusMessage)
        {
            _statusMessage = frame.StatusMessage;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public ObservableCollection<LyricsSourceItem> LyricsSources { get; }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public void MoveLyricsSource(LyricsSourceItem item, int delta)
    {
        var index = LyricsSources.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= LyricsSources.Count)
            return;

        LyricsSources.Move(index, target);

        _settings.LyricsSources = LyricsSources
            .Select(entry => new LyricsSourceOption
            {
                Source = entry.Source,
                Enabled = entry.Enabled
            })
            .ToList();

        RefreshLyricsSourceMoveState();
        PersistSettings();
        ApplyLyricsSourceConfiguration();
    }

    private void OnLyricsSourceToggled(LyricsSourceItem item)
    {
        var option = _settings.LyricsSources.FirstOrDefault(
            entry => entry.Source == item.Source);
        if (option is null)
            return;

        option.Enabled = item.Enabled;
        PersistSettings();
        ApplyLyricsSourceConfiguration();
    }

    private void RefreshLyricsSourceMoveState()
    {
        for (var index = 0; index < LyricsSources.Count; index++)
        {
            LyricsSources[index].CanMoveUp = index > 0;
            LyricsSources[index].CanMoveDown = index < LyricsSources.Count - 1;
        }
    }

    private void ApplyLyricsSourceConfiguration()
    {
        _lyricsCoordinator.ApplyConfiguration(
            LyricsSources
                .Where(item => item.Enabled)
                .Select(item => item.Source)
                .ToArray());
    }

    private void PersistSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private (byte Red, byte Green, byte Blue) ReadTextRgb()
    {
        var color = _settings.TextColor;
        if (color.StartsWith('#'))
            color = color[1..];
        if (color.Length == 8)
            color = color[2..];

        if (color.Length == 6 &&
            byte.TryParse(color[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) &&
            byte.TryParse(color.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) &&
            byte.TryParse(color.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            return (red, green, blue);

        return (255, 255, 255);
    }

    private void SetTextRgb(byte? red = null, byte? green = null, byte? blue = null)
    {
        var current = ReadTextRgb();
        SetTextColor($"#FF{red ?? current.Red:X2}{green ?? current.Green:X2}{blue ?? current.Blue:X2}");
    }

    private static byte NormalizeColorChannel(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0, 255));
}
