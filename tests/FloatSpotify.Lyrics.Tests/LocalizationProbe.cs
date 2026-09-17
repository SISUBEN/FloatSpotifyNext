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

/// <summary>
/// i18n 冒烟测试：真的把窗口建出来，检查 <c>{loc:Tr}</c> 到底有没有生效。
/// <para>
/// 编译通过**不能**说明标记扩展是对的 —— <c>{loc:Tr}</c> 返回的是绑定，写错了要到
/// 运行时构造窗口才炸（那样整个程序根本起不来）。所以必须真建窗口。
/// </para>
/// <para>
/// 运行：<c>FloatSpotify.Lyrics.Tests.exe --i18n-probe</c>
/// </para>
/// </summary>
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

        // 歌词窗先出来（摆在屏幕外）：ControlsWindow 会把自己挂成它的 Owner 并吸附到它旁边，
        // Owner 未显示过的话 WPF 会直接抛异常。
        var lyricsWindow = new LyricsWindow(vm);
        Show(lyricsWindow);

        var controls = new ControlsWindow(vm, lyricsWindow);
        Show(controls);
        Park(controls);

        var setup = new SpotifySetupWindow { Owner = controls };
        Show(setup);

        // 建窗口这一步本身就是断言：标记扩展写错会直接抛 XamlParseException。
        Check(true, "controls and setup windows construct and lay out");

        var combo = FindLanguageComboBox(controls);
        Check(combo is not null, "language ComboBox found in the settings panel");

        // ── 英文 ───────────────────────────────────────────────
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

        // 下拉项的文案走 DisplayMemberPath，不落在 Content 上，得直接问选项对象。
        Check(LanguageNames(vm).SequenceEqual(new[] { "System default", "简体中文", "English" }),
            "language option names are English-mode");

        // ── 中文 ───────────────────────────────────────────────
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

        // 歌词源那几行是 DataTemplate 生成的，也要跟着换语言。
        Check(chinese.Any(text => text.Contains("用的是非公开接口")),
            "lyric source tooltip follows the language");

        // 收起状态的 ComboBox 显示的是选中项，必须走模板绑定而不是快照。
        Check(combo!.SelectionBoxItem is LanguageOption { DisplayName: "跟随系统" },
            "collapsed ComboBox shows the re-translated option, not a stale snapshot");

        Loc.Language = AppLanguage.English;
        Refresh(controls, setup);
        Check(combo.SelectionBoxItem is LanguageOption { DisplayName: "System default" },
            "collapsed ComboBox re-translates back to English");

        // 全表体检：任何一个 key 在任一语言下取不到译文，都会在界面上原样露出 key。
        // 引擎状态、托盘菜单这些窗口扫不到的 key 也一并覆盖 —— 窗口测试够不着它们。
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

        // 落盘：选中的语言要能存住，「跟随系统」要存成 null 而不是某个具体值。
        store.Save(new AppSettings { Language = AppLanguage.English });
        Check(store.Load().Language == AppLanguage.English, "an explicit language survives settings.json");
        store.Save(new AppSettings { Language = null });
        Check(store.Load().Language is null, "follow-system round-trips as null");

        // 留两张图给人工过目：新增的「语言」那一行有没有把排版挤坏。
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

    /// <summary>窗口摆到屏幕外再 Show —— 不进视觉树的话 ItemsControl 不会生成子项，扫不到文案。</summary>
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
            // 数据模板里的绑定是异步派发的，先跑一轮 Dispatcher 让它落地。
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            window.UpdateLayout();
        }
    }

    /// <summary>把窗口内容渲成 PNG，人工看一眼排版（自动化断言查不出「挤在一起」）。</summary>
    private static void Capture(Window window, string path)
    {
        if (window.Content is not FrameworkElement root)
            return;

        root.Measure(new Size(460, 4000));
        root.Arrange(new Rect(0, 0, 460, root.DesiredSize.Height));
        root.UpdateLayout();

        var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        // 2 倍 DPI：小字号下的细节（下划线、边框）在 1 倍图里看不清，容易误判成渲染坏了。
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

            // ToolTip 不进视觉树（弹出时才建），得从属性上直接读。
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

            // 逻辑树里混着 RowDefinition 这类非 Visual 的 DependencyObject，
            // VisualTreeHelper 遇到它们会直接抛，得先挡掉。
            if (node is not Visual and not System.Windows.Media.Media3D.Visual3D)
                continue;

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var index = 0; index < count; index++)
                pending.Push(VisualTreeHelper.GetChild(node, index));
        }
    }

    /// <summary>
    /// 漏翻时 <see cref="Loc.T"/> 会把 key 原样吐出来。key 的形态是
    /// <c>Area_Thing</c>（纯 ASCII 单词 + 下划线），真实文案里不会长这样。
    /// </summary>
    private static bool LooksLikeRawKey(string text) =>
        !text.Contains(' ') &&
        text.Contains('_') &&
        text.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') &&
        char.IsUpper(text[0]);
}
