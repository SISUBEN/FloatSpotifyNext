using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace FloatSpotify.Playback;

public sealed class DemoPlaybackEngine : IPlaybackEngine
{
    private readonly object _sync = new();
    private readonly DemoTrack[] _tracks =
    [
        new(
            "Rewrite Preview",
            "FloatSpotify",
            TimeSpan.FromSeconds(32),
            [
                new(0, "Click the lyric to open controls"),
                new(4, "Drag anywhere on the lyric to move it"),
                new(8, "Tune the type, glow and opacity in place"),
                new(12, "Lock the lyric when the position feels right"),
                new(16, "Locked lyrics let every click pass through"),
                new(20, "Use the tray icon whenever you need it back"),
                new(24, "Spotify will plug into this same playback seam"),
                new(28, "No Python process and no permanent localhost server")
            ]),
        new(
            "桌面歌词",
            "FloatSpotify",
            TimeSpan.FromSeconds(28),
            [
                new(0, "歌词本身就是主界面"),
                new(4, "点击歌词，操作就地展开"),
                new(8, "移动、缩放、换色都不离开当前屏幕"),
                new(12, "锁定以后，鼠标不会被悬浮窗挡住"),
                new(16, "播放状态和错误状态会被明确区分"),
                new(20, "网络恢复后，歌词会自动继续"),
                new(24, "这是重写版本的第一个交互原型")
            ])
    ];

    private int _trackIndex;
    private bool _isPlaying = true;
    private TimeSpan _position;
    private long _lastTimestamp = Stopwatch.GetTimestamp();
    private double _lyricOffsetSeconds;

    public async IAsyncEnumerable<PlaybackFrame> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            PlaybackFrame frame;

            lock (_sync)
            {
                AdvancePosition();
                frame = CreateFrame();
            }

            yield return frame;
        }
    }

    public Task ExecuteAsync(
        PlayerCommand command,
        double value = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            AdvancePosition();

            switch (command)
            {
                case PlayerCommand.TogglePlayback:
                    _isPlaying = !_isPlaying;
                    break;
                case PlayerCommand.PreviousTrack:
                    _trackIndex = _position > TimeSpan.FromSeconds(3)
                        ? _trackIndex
                        : (_trackIndex - 1 + _tracks.Length) % _tracks.Length;
                    _position = TimeSpan.Zero;
                    break;
                case PlayerCommand.NextTrack:
                    _trackIndex = (_trackIndex + 1) % _tracks.Length;
                    _position = TimeSpan.Zero;
                    break;
                case PlayerCommand.SetLyricOffset:
                    _lyricOffsetSeconds = value;
                    break;
            }

            _lastTimestamp = Stopwatch.GetTimestamp();
        }

        return Task.CompletedTask;
    }

    private void AdvancePosition()
    {
        var now = Stopwatch.GetTimestamp();

        if (_isPlaying)
        {
            _position += Stopwatch.GetElapsedTime(_lastTimestamp, now);

            if (_position >= _tracks[_trackIndex].Duration)
            {
                _trackIndex = (_trackIndex + 1) % _tracks.Length;
                _position = TimeSpan.Zero;
            }
        }

        _lastTimestamp = now;
    }

    private PlaybackFrame CreateFrame()
    {
        var track = _tracks[_trackIndex];
        var lyricPosition = _position + TimeSpan.FromSeconds(_lyricOffsetSeconds);
        var currentIndex = -1;

        for (var index = 0; index < track.Lines.Length; index++)
        {
            if (track.Lines[index].At <= lyricPosition)
                currentIndex = index;
            else
                break;
        }

        var currentLine = currentIndex >= 0 ? track.Lines[currentIndex].Text : "...";
        var nextLine = currentIndex + 1 < track.Lines.Length
            ? track.Lines[currentIndex + 1].Text
            : string.Empty;

        return new PlaybackFrame(
            track.Title,
            track.Artist,
            currentLine,
            nextLine,
            _isPlaying,
            _position,
            track.Duration);
    }

    private sealed record DemoTrack(
        string Title,
        string Artist,
        TimeSpan Duration,
        TimedLine[] Lines);

    private sealed record TimedLine(TimeSpan At, string Text)
    {
        public TimedLine(double seconds, string text)
            : this(TimeSpan.FromSeconds(seconds), text)
        {
        }
    }
}
