using System.Windows;
using System.Windows.Controls;
using FloatSpotify.ViewModels;

namespace FloatSpotify.Windows;

public partial class ControlsWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly LyricsWindow _lyricsWindow;
    private bool _allowClose;

    public ControlsWindow(OverlayViewModel viewModel, LyricsWindow lyricsWindow)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _lyricsWindow = lyricsWindow;
        DataContext = viewModel;
        Owner = lyricsWindow;

        SizeChanged += (_, _) => AnchorToLyrics();
        _lyricsWindow.LocationChanged += (_, _) => AnchorToLyrics();
        _lyricsWindow.SizeChanged += (_, _) => AnchorToLyrics();
        Closing += OnClosing;
    }

    public void ToggleForLyrics()
    {
        if (IsVisible)
        {
            HideControls();
            return;
        }

        ShowForLyrics(false);
    }

    public void ShowForLyrics(bool showSettings)
    {
        _viewModel.IsSettingsOpen = showSettings;

        if (!IsVisible)
            Show();

        AnchorToLyrics();
        Activate();
    }

    public void HideControls()
    {
        _viewModel.IsSettingsOpen = false;
        Hide();
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private void AnchorToLyrics()
    {
        if (!IsVisible || !_lyricsWindow.IsVisible) return;

        var left = _lyricsWindow.Left + (_lyricsWindow.ActualWidth - ActualWidth) / 2;
        var below = _lyricsWindow.Top + _lyricsWindow.ActualHeight + 8;
        var virtualBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        var top = below + ActualHeight <= virtualBottom
            ? below
            : _lyricsWindow.Top - ActualHeight - 8;

        Left = Math.Clamp(
            left,
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth);
        Top = Math.Clamp(
            top,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight);
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.PreviousTrackAsync();
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.TogglePlaybackAsync();
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.NextTrackAsync();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsSettingsOpen = !_viewModel.IsSettingsOpen;
        Dispatcher.BeginInvoke(AnchorToLyrics);
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleLock();
        HideControls();
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RequestHide();
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string color })
            _viewModel.SetTextColor(color);
    }

    private void MoveLyricsSourceUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LyricsSourceItem item })
            _viewModel.MoveLyricsSource(item, -1);
    }

    private void MoveLyricsSourceDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LyricsSourceItem item })
            _viewModel.MoveLyricsSource(item, 1);
    }

    private void ResetLyricOffsetButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetLyricOffset();
    }

    private void ClientIdHelpButton_Click(object sender, RoutedEventArgs e)
    {
        new SpotifySetupWindow { Owner = this }.ShowDialog();
    }

    private async void ReauthorizeButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ReauthorizeSpotifyAsync();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;

        e.Cancel = true;
        HideControls();
    }
}
