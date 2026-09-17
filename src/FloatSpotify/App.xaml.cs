using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using FloatSpotify.Playback;
using FloatSpotify.Storage;
using FloatSpotify.ViewModels;
using FloatSpotify.Windows;
using Forms = System.Windows.Forms;

namespace FloatSpotify;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private CancellationTokenSource? _lifetime;
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _trayIconImage;
    private PlaybackCoordinator? _playbackEngine;
    private OverlayViewModel? _viewModel;
    private LyricsWindow? _lyricsWindow;
    private ControlsWindow? _controlsWindow;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, "Local\\FloatSpotify.Next", out var isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show(
                "FloatSpotify Next is already running. Use its tray icon to restore the lyric.",
                "FloatSpotify",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();

        // 取词诊断默认关闭。放在这里而不是协调器里：这是「进程启动了」这件事，
        // 而且要让日志文件立刻出现 —— 否则用户设好开关却看不到文件，会以为没生效。
        LyricsDiagnostics.AnnounceStartup();

        // 歌词源协调器只建一个，两个播放引擎共用 —— 同一时刻只有一个引擎在跑，
        // 共用可以保证「启用哪些源、什么顺序」这份配置只有一处真相。
        var lyricsCoordinator = new LyricsCoordinator();

        _playbackEngine = new PlaybackCoordinator(
            new SpotifyPlaybackEngine(() => settings.SpotifyClientId, lyricsCoordinator),
            new YouTubeMusicPlaybackEngine(lyricsCoordinator),
            settings.PlaybackSource);

        _viewModel = new OverlayViewModel(_playbackEngine, settingsStore, settings, lyricsCoordinator);
        _lyricsWindow = new LyricsWindow(_viewModel);
        _lyricsWindow.ShowOverlay();
        _controlsWindow = new ControlsWindow(_viewModel, _lyricsWindow);

        _viewModel.ToggleControlsRequested += ToggleControls;
        _viewModel.SettingsRequested += ShowSettings;
        _viewModel.HideRequested += HideOverlay;
        _viewModel.ExitRequested += ExitApplication;

        CreateTrayIcon();

        _lifetime = new CancellationTokenSource();
        _ = _viewModel.RunAsync(_lifetime.Token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _playbackEngine?.Dispose();
        // 先释放托盘图标，再释放它引用的 GDI 图标句柄。
        _trayIcon?.Dispose();
        _trayIconImage?.Dispose();

        if (_instanceMutex != null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示歌词", null, (_, _) => RunOnUiThread(ShowOverlay));
        menu.Items.Add("打开设置", null, (_, _) => RunOnUiThread(ShowSettings));
        menu.Items.Add("解锁歌词", null, (_, _) => RunOnUiThread(UnlockOverlay));
        menu.Items.Add("重新授权 Spotify", null, (_, _) => RunOnUiThread(ReauthorizeSpotify));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => RunOnUiThread(ExitApplication));

        _trayIconImage = LoadTrayIcon();

        _trayIcon = new Forms.NotifyIcon
        {
            // 取不到资源时退回系统图标，保证托盘不会空白。
            Icon = _trayIconImage ?? SystemIcons.Information,
            Text = "FloatSpotify Next",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => RunOnUiThread(ShowOverlay);
    }

    /// <summary>
    /// 从嵌入资源里取出 Assets/app.ico，并按当前 DPI 挑选最合适的那一帧
    /// （托盘是小图标，直接用 256px 那一帧会被系统粗暴缩放，边缘发虚）。
    /// </summary>
    private static Icon? LoadTrayIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("Assets/app.ico", UriKind.Relative));

            if (resource is null) return null;

            using var stream = resource.Stream;
            return new Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return null;
        }
    }

    private void RunOnUiThread(Action action)
    {
        Dispatcher.BeginInvoke(action);
    }

    private void ToggleControls()
    {
        if (_viewModel?.IsLocked == true) return;
        _controlsWindow?.ToggleForLyrics();
    }

    private void ShowOverlay()
    {
        _lyricsWindow?.ShowOverlay();
    }

    private void ShowSettings()
    {
        _lyricsWindow?.ShowOverlay();
        _controlsWindow?.ShowForLyrics(true);
    }

    private void UnlockOverlay()
    {
        _viewModel?.Unlock();
        _lyricsWindow?.ShowOverlay();
    }

    private async void ReauthorizeSpotify()
    {
        if (_viewModel is null) return;

        _lyricsWindow?.ShowOverlay();
        _controlsWindow?.ShowForLyrics(true);
        await _viewModel.ReauthorizeSpotifyAsync();
    }

    private void HideOverlay()
    {
        _controlsWindow?.HideControls();
        _lyricsWindow?.Hide();
    }

    private void ExitApplication()
    {
        if (_isExiting) return;
        _isExiting = true;

        _lifetime?.Cancel();
        _trayIcon?.Dispose();

        _controlsWindow?.AllowClose();
        _lyricsWindow?.AllowClose();
        _controlsWindow?.Close();
        _lyricsWindow?.Close();
        Shutdown();
    }
}
