using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FloatSpotify.Localization;
using FloatSpotify.Playback;
using FloatSpotify.Storage;
using FloatSpotify.ViewModels;
using FloatSpotify.Windows;

internal static class LocalizationProbe
{
    private static int _passed;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
        _passed++;
    }

    public static void Probe()
    {
        var app = Application.Current ?? new Application();
        app.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();

        using var playback = new PlaybackCoordinator(
            new DemoPlaybackEngine(), new DemoPlaybackEngine(), PlaybackSource.Spotify);
        var settings = new AppSettings();
        var store = new SettingsStore(Path.Combine(
            Path.GetTempPath(), "FloatSpotify-i18n-" + Guid.NewGuid() + ".json"));
        var vm = new OverlayViewModel(
            playback, store, settings, new LyricsCoordinator(Array.Empty<ILyricsProvider>()));

        vm.IsSettingsOpen = true;

        var lyricsWindow = new LyricsWindow(vm);
        Show(lyricsWindow);

        var controls = new ControlsWindow(vm, lyricsWindow);
        Show(controls);
        Park(controls);

        var setup = new SpotifySetupWindow { Owner = controls };
        Show(setup);

        Check(true, "controls and setup windows construct and lay out");

        var combo = FindLanguageComboBox(controls);
        Check(combo is not null, "language ComboBox found in the settings panel");

        Loc.Language = AppLanguage.English;
        Refresh(controls, setup);
        var english = Collect(controls).Concat(Collect(setup)).ToList();

        Check(english.Contains("FloatSpotify Controls"), "window title switches to English");
        Check(english.Contains("Set up your Spotify Client ID"), "setup window heading is English");
        foreach (var expected in new[]
                 {
                     "Language", "Playback source", "Font size", "Lyric width", "Screen position",
                     "Font", "Opacity", "Text color", "Lyric offset", "Lyric sources",
                     "Next line", "Outline shadow", "Always on top",
                     "Authorize Spotify with this Client ID", "System default",
                     "LRCLIB", "Karalyr · word-level", "NetEase Cloud Music", "Public API",
                     "Unofficial API", "Free drag", "Bottom right", "Copy callback URL",
                     "Open the Spotify Dashboard", "Close", "Previous track", "Hide the lyrics"
                 })
            Check(english.Contains(expected), $"English text present: {expected}");

        Check(!english.Any(LooksLikeRawKey), "no untranslated key leaked into the English UI");

        Check(LanguageNames(vm).SequenceEqual(new[] { "System default", "简体中文", "English" }),
            "language option names are English-mode");

        Loc.Language = AppLanguage.ChineseSimplified;
        Refresh(controls, setup);
        var chinese = Collect(controls).Concat(Collect(setup)).ToList();

        Check(chinese.Contains("FloatSpotify 控制条"), "window title switches to Chinese");
        foreach (var expected in new[]
                 {
                     "语言", "播放源", "字号", "歌词宽度", "屏幕位置", "字体", "透明度",
                     "文字颜色", "歌词偏移", "歌词源", "下一句", "描边阴影", "始终置顶",
                     "使用此 Client ID 授权 Spotify", "跟随系统", "网易云音乐",
                     "公开接口", "非官方接口", "自由拖动", "右下角", "复制回调地址",
                     "打开 Spotify Dashboard", "关闭", "上一首", "隐藏歌词"
                 })
            Check(chinese.Contains(expected), $"Chinese text present: {expected}");

        Check(!chinese.Any(LooksLikeRawKey), "no untranslated key leaked into the Chinese UI");
        Check(!chinese.Contains("Playback source"), "English label is gone after switching to Chinese");
        Check(!english.Contains("播放源"), "Chinese label is gone in English");

        Check(LanguageNames(vm).SequenceEqual(new[] { "跟随系统", "简体中文", "English" }),
            "language option names follow the language while the language's own name does not");

        Check(chinese.Any(text => text.Contains("用的是非公开接口")),
            "lyric source tooltip follows the language");

        Check(combo!.SelectionBoxItem is LanguageOption { DisplayName: "跟随系统" },
            "collapsed ComboBox shows the re-translated option, not a stale snapshot");

        Loc.Language = AppLanguage.English;
        Refresh(controls, setup);
        Check(combo.SelectionBoxItem is LanguageOption { DisplayName: "System default" },
            "collapsed ComboBox re-translates back to English");

        var table = (System.Collections.IDictionary)typeof(Loc).Assembly
            .GetType("FloatSpotify.Localization.Strings")!
            .GetField("Table", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var broken = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in table)
        {
            var key = (string)entry.Key;
            foreach (var language in new[] { AppLanguage.ChineseSimplified, AppLanguage.English })
            {
                Loc.Language = language;
                var text = Loc.T(key);
                if (string.IsNullOrWhiteSpace(text) || text == key || LooksLikeRawKey(text))
                    broken.Add($"{key}/{language}");
            }
        }

        Check(broken.Count == 0,
            $"all {table.Count} keys resolve in both languages"
            + (broken.Count == 0 ? string.Empty : " -> broken: " + string.Join(", ", broken)));

        store.Save(new AppSettings { Language = AppLanguage.English });
        Check(store.Load().Language == AppLanguage.English, "an explicit language survives settings.json");
        store.Save(new AppSettings { Language = null });
        Check(store.Load().Language is null, "follow-system round-trips as null");

        var output = Path.Combine(Path.GetTempPath(), "FloatSpotify-i18n");
        Directory.CreateDirectory(output);
        Loc.Language = AppLanguage.ChineseSimplified;
        Capture(controls, Path.Combine(output, "controls-zh.png"));
        Loc.Language = AppLanguage.English;
        Capture(controls, Path.Combine(output, "controls-en.png"));

        setup.Close();
        controls.AllowClose();
        controls.Close();
        lyricsWindow.AllowClose();
        lyricsWindow.Close();

        Console.WriteLine($"Passed {_passed} i18n checks.");
    }

    private static void Show(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        Park(window);
        window.ShowActivated = false;
        window.Show();
    }

    private static void Park(Window window)
    {
        window.Left = -20000;
        window.Top = -20000;
    }

    private static void Refresh(params Window[] windows)
    {
        foreach (var window in windows)
        {
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            window.UpdateLayout();
        }
    }

    private static void Capture(Window window, string path)
    {
        if (window.Content is not FrameworkElement root)
            return;

        root.Measure(new Size(460, 4000));
        root.Arrange(new Rect(0, 0, 460, root.DesiredSize.Height));
        root.UpdateLayout();

        var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);

        Console.WriteLine($"Rendered {path} ({width}x{height})");
    }

    private static IEnumerable<string> LanguageNames(OverlayViewModel vm) =>
        vm.LanguageOptions.Select(option => option.DisplayName);

    private static ComboBox? FindLanguageComboBox(DependencyObject root)
    {
        foreach (var node in Walk(root))
            if (node is ComboBox { ItemsSource: IReadOnlyList<LanguageOption> })
                return (ComboBox)node;

        return null;
    }

    private static List<string> Collect(DependencyObject root)
    {
        var result = new List<string>();

        foreach (var node in Walk(root))
        {
            switch (node)
            {
                case Window window:
                    result.Add(window.Title);
                    break;
                case TextBlock block:
                    result.Add(block.Text);
                    break;
                case ContentControl { Content: string content }:
                    result.Add(content);
                    break;
            }

            var toolTip = node switch
            {
                FrameworkElement { ToolTip: string tip } => tip,
                FrameworkContentElement { ToolTip: string tip } => tip,
                _ => null
            };

            if (toolTip is not null)
                result.Add(toolTip);
        }

        return result.Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node))
                continue;

            yield return node;

            foreach (var child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject logical)
                    pending.Push(logical);

            if (node is not Visual and not System.Windows.Media.Media3D.Visual3D)
                continue;

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var index = 0; index < count; index++)
                pending.Push(VisualTreeHelper.GetChild(node, index));
        }
    }

    private static bool LooksLikeRawKey(string text) =>
        !text.Contains(' ') &&
        text.Contains('_') &&
        text.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') &&
        char.IsUpper(text[0]);
}
