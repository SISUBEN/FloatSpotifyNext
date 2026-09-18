using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using FloatSpotify.Localization;
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

        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();

        Loc.Language = Loc.Resolve(Loc.ToChoice(settings.Language));

        _instanceMutex = new Mutex(true, "Local\\FloatSpotify.Next", out var isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show(
                Loc.T("App_AlreadyRunning_Message"),
                Loc.T("App_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        LyricsDiagnostics.AnnounceStartup();

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
        _trayIconImage = LoadTrayIcon();

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayIconImage ?? SystemIcons.Information,
            Text = "FloatSpotify Next",
            Visible = true,
            ContextMenuStrip = CreateTrayMenu()
        };
        _trayIcon.DoubleClick += (_, _) => RunOnUiThread(ShowOverlay);

        Loc.LanguageChanged += () => RunOnUiThread(RebuildTrayMenu);
    }

    private static Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("Tray_ShowLyrics"), null, (_, _) => Instance?.ShowOverlay());
        menu.Items.Add(Loc.T("Tray_OpenSettings"), null, (_, _) => Instance?.ShowSettings());
        menu.Items.Add(Loc.T("Tray_UnlockLyrics"), null, (_, _) => Instance?.UnlockOverlay());
        menu.Items.Add(Loc.T("Tray_RefreshLyrics"), null, (_, _) => Instance?.RefreshLyrics());
        menu.Items.Add(Loc.T("Tray_ReauthorizeSpotify"), null, (_, _) => Instance?.ReauthorizeSpotify());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Tray_Exit"), null, (_, _) => Instance?.ExitApplication());
        return menu;
    }

    private static App? Instance => System.Windows.Application.Current as App;

    private void RebuildTrayMenu()
    {
        if (_trayIcon is null || _isExiting)
            return;

        var previous = _trayIcon.ContextMenuStrip;
        _trayIcon.ContextMenuStrip = CreateTrayMenu();
        previous?.Dispose();
    }

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

    private async void RefreshLyrics()
    {
        if (_viewModel is null) return;

        _lyricsWindow?.ShowOverlay();
        await _viewModel.RefreshLyricsAsync();
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
