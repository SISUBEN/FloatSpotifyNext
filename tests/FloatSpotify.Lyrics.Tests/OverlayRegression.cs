using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using FloatSpotify.Playback;
using FloatSpotify.Storage;
using FloatSpotify.ViewModels;
using FloatSpotify.Windows;

internal static class OverlayRegression
{
    public static void Probe()
    {
        var app = Application.Current ?? new Application();
        app.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
        using var playback = new PlaybackCoordinator(new DemoPlaybackEngine(), new DemoPlaybackEngine(), PlaybackSource.Spotify);
        var settings = new AppSettings { OverlayWidth = 400, IsLocked = false, ShowNextLine = false };
        var vm = new OverlayViewModel(playback, new SettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FloatSpotify-overlay-" + Guid.NewGuid() + ".json")), settings, new LyricsCoordinator(Array.Empty<ILyricsProvider>()));
        var window = new LyricsWindow(vm);
        window.Left = -10000; window.Top = -10000; window.ShowActivated = false; window.Show();
        var apply = typeof(OverlayViewModel).GetMethod("ApplyFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
        apply.Invoke(vm, new object[] { new PlaybackFrame("No Lyrics Song", "Artist", "Artist · No Lyrics Song", "暂未找到歌词，将自动重试",
            false, TimeSpan.Zero, TimeSpan.FromSeconds(100)) });
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(400, 500)); root.Arrange(new Rect(0, 0, 400, root.DesiredSize.Height)); root.UpdateLayout();
        var scroller = (ScrollViewer)window.FindName("LyricScroller");
        var lyric = (KaraokeText)scroller.Content;
        Console.WriteLine($"SURFACE height={root.ActualHeight}; lyric={lyric.ActualWidth}x{lyric.ActualHeight}; viewport={scroller.ViewportWidth}x{scroller.ViewportHeight}; text={lyric.Frame?.CurrentLine}");
        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        var target = window.InputHitTest(new Point(window.ActualWidth / 2, window.ActualHeight / 2)) as UIElement ?? scroller;
        Console.WriteLine("HIT " + target.GetType().Name);
        target.RaiseEvent(down);
        if (!down.Handled)
        {
            down.RoutedEvent = Mouse.MouseDownEvent;
            target.RaiseEvent(down);
        }
        var dragging = (bool)typeof(LyricsWindow).GetField("_pointerDown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Console.WriteLine($"DRAG routed event handled={down.Handled}; pointerDown={dragging}");
        if (!dragging) throw new Exception("No-lyrics content swallowed drag start");
        var clicks = 0;
        vm.ToggleControlsRequested += () => clicks++;
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
        if (clicks != 1) throw new Exception("No-lyrics click failed to open controls");
        Console.WriteLine("PASS: no-lyrics click opens controls after capture release");
        apply.Invoke(vm, new object[] { new PlaybackFrame("", "", "", "", false, TimeSpan.Zero, TimeSpan.Zero) });
        root.UpdateLayout();
        if (string.IsNullOrWhiteSpace(lyric.Frame?.CurrentLine)) throw new Exception("Blank status became invisible");
        Console.WriteLine("PASS: empty status has visible fallback text");
        var plainText = string.Join("\n", Enumerable.Repeat("未同步歌词 scrollable text", 30));
        var plain = new TimedLyric(TimeSpan.Zero, plainText) { IsPlainText = true };
        apply.Invoke(vm, new object[] { new PlaybackFrame("Plain song", "Artist", plainText, "", false, TimeSpan.Zero, TimeSpan.Zero, ActiveLyric: plain) });
        root.Measure(new Size(400, 600)); root.Arrange(new Rect(0, 0, 400, root.DesiredSize.Height)); root.UpdateLayout();
        if (!scroller.IsHitTestVisible || scroller.ActualHeight > 300.1) throw new Exception("Plain text scrolling viewport broken");
        scroller.ScrollToBottom(); root.UpdateLayout();
        apply.Invoke(vm, new object[] { new PlaybackFrame("Missing song", "Artist", "未找到歌词", "", false, TimeSpan.Zero, TimeSpan.Zero) });
        root.UpdateLayout();
        if (scroller.IsHitTestVisible || scroller.VerticalOffset > 0.1 || lyric.ActualHeight < 10)
            throw new Exception("Transition from scrolled plain lyrics leaves missing text clipped or blocks hit target");
        Console.WriteLine("PASS: scrolled plain -> missing lyrics resets viewport and restores drag surface");
        if (LyricsWindow.CanStartDrag(new System.Windows.Controls.Primitives.ScrollBar(), false) ||
            LyricsWindow.CanStartDrag(new Border(), true) || !LyricsWindow.CanStartDrag(new Border(), false))
            throw new Exception("Drag policy breaks scrollbars/lock");
        Console.WriteLine("PASS: scrollbars retain gestures and lock retains pass-through policy");
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)Math.Ceiling(root.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        if (!pixels.Where((_, i) => i % 4 != 3).Any(b => b > 100)) throw new Exception("Actual XAML missing-state text is transparent");
        Console.WriteLine("PASS: actual XAML no-lyrics state has visible rendered pixels");
        window.AllowClose(); window.Close();
        if (!dragging) throw new Exception("No-lyrics content swallowed drag start");
        if (lyric.ActualHeight < 10) throw new Exception("No-lyrics content collapsed");
    }
}
