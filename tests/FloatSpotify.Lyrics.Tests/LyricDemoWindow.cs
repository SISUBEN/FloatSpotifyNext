using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FloatSpotify.Playback;
using FloatSpotify.Windows;

/// <summary>Interactive fixture using the production renderer, not a replacement UI.</summary>
internal sealed class LyricDemoWindow : Window
{
    private const double Duration = 16;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly KaraokeText _lyric = new() { FontSize = 43, FontWeight = FontWeights.SemiBold,
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), Foreground = Brushes.White };
    private readonly TextBlock _next = new() { FontSize = 19, Foreground = new SolidColorBrush(Color.FromRgb(133, 139, 150)),
        TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
    private readonly TextBlock _mode = new() { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(83, 221, 153)) };
    private readonly TextBlock _time = new() { FontSize = 12, Foreground = Brushes.LightGray, Width = 90,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Slider _seek = new() { Minimum = 0, Maximum = Duration, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _pause = new() { Content = "暂停", Width = 88, Height = 32 };
    private readonly Border _surface;
    private readonly TimedLyric[] _lines;
    private bool _playing = true;
    private bool _updatingSlider;
    private double _position;
    private long _lastTick;
    private int _captureCount;
    private readonly string? _captureDirectory;

    public LyricDemoWindow(string? captureDirectory)
    {
        Title = "FloatSpotify · 实时逐字高亮演示";
        Width = 880; Height = 480; MinWidth = 640; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(16, 18, 24));
        Foreground = Brushes.White;
        _captureDirectory = captureDirectory;
        if (captureDirectory is not null) Directory.CreateDirectory(captureDirectory);
        _lines = LrcParser.Parse(
            "[00:00.00]<00:00.70>让<00:01.00>每<00:01.35>一<00:01.70>个<00:02.05>字，<00:02.65>跟<00:03.00>着<00:03.35>节<00:03.70>奏<00:04.05>亮<00:04.50>起<00:04.95>来<00:05.50>\n" +
            "[00:06.00]<00:06.40>Every <00:07.20>word <00:07.95>comes <00:08.70>alive <00:09.55>in <00:10.10>time<00:11.20>\n" +
            "[00:12.00]没有逐字时间轴时，保留整行歌词", duration: TimeSpan.FromSeconds(Duration)).ToArray();

        _surface = new Border { Padding = new Thickness(34, 28, 34, 24),
            Background = new SolidColorBrush(Color.FromRgb(22, 24, 31)) };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "FLOATSPOTIFY  /  LIVE LYRICS", FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(83, 221, 153)) });
        header.Children.Add(new TextBlock { Text = "逐字高亮 · 实时演示", FontSize = 25, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 10) });
        header.Children.Add(_mode);
        grid.Children.Add(header);
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        center.Children.Add(_lyric); center.Children.Add(_next);
        Grid.SetRow(center, 1); grid.Children.Add(center);

        var footer = new StackPanel();
        var transport = new Grid();
        transport.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        transport.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        transport.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        transport.Children.Add(_pause);
        Grid.SetColumn(_seek, 1); transport.Children.Add(_seek);
        Grid.SetColumn(_time, 2); transport.Children.Add(_time);
        footer.Children.Add(transport);
        footer.Children.Add(new TextBlock { Text = "可暂停 / 拖动进度 · 16 秒循环 · 自建演示时间轴，无音频 · 使用主程序 WPF 渲染器",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 16, 0, 0) });
        Grid.SetRow(footer, 2); grid.Children.Add(footer);
        _surface.Child = grid; Content = _surface;
        _pause.Click += (_, _) => { Advance(); _playing = !_playing; _pause.Content = _playing ? "暂停" : "播放"; Publish(); };
        _seek.ValueChanged += (_, _) => { if (_updatingSlider) return; _position = _seek.Value; _lastTick = _clock.ElapsedTicks; Publish(); };
        _playbackTimer.Tick += (_, _) => { Advance(); Publish(); };
        _captureTimer.Tick += (_, _) => Capture();
        Loaded += (_, _) =>
        {
            _lastTick = _clock.ElapsedTicks;
            Publish(); _playbackTimer.Start();
            if (_captureDirectory is not null)
            {
                File.WriteAllText(Path.Combine(_captureDirectory, "ready.txt"), $"PID={Environment.ProcessId}; Renderer={_lyric.GetType().FullName}; Visible={IsVisible}");
                _captureTimer.Start();
            }
        };
        Closed += (_, _) => { _playbackTimer.Stop(); _captureTimer.Stop(); };
    }

    private void Advance()
    {
        var now = _clock.ElapsedTicks;
        if (_playing) _position = (_position + (now - _lastTick) / (double)Stopwatch.Frequency) % Duration;
        _lastTick = now;
    }

    private void Publish()
    {
        var index = _position < 6 ? 0 : _position < 12 ? 1 : 2;
        var line = _lines[index];
        _lyric.Frame = new PlaybackFrame("Realtime demo", "FloatSpotify", line.Text, "", _playing,
            TimeSpan.FromSeconds(_position), TimeSpan.FromSeconds(Duration), ActiveLyric: line);
        _mode.Text = index == 0 ? "中文逐字 / 字内连续扫亮" : index == 1 ? "英文逐词 / 随时间轴渐进高亮" : "Fallback / 整行显示，不伪造逐字时间";
        _next.Text = _lines[(index + 1) % _lines.Length].Text;
        _updatingSlider = true; _seek.Value = _position; _updatingSlider = false;
        _time.Text = $"{_position:00.0} / 16.0 s";
    }

    private void Capture()
    {
        if (_captureDirectory is null) return;
        if (_captureCount >= 320)
        {
            _captureTimer.Stop();
            File.WriteAllText(Path.Combine(_captureDirectory, "capture-complete.txt"), $"Frames={_captureCount}; Live WPF visual capture");
            return;
        }
        var bitmap = new RenderTargetBitmap((int)_surface.ActualWidth, (int)_surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(_captureDirectory, $"frame-{_captureCount++:D4}.png"));
        encoder.Save(stream);
    }
}
