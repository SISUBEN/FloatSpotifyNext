using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using FloatSpotify.Localization;

namespace FloatSpotify.Windows;

public partial class SpotifySetupWindow : Window
{
    private const string CallbackUri = "http://127.0.0.1:8888/callback";
    private const string DashboardUri = "https://developer.spotify.com/dashboard";

    private bool _callbackCopied;

    public SpotifySetupWindow()
    {
        InitializeComponent();

        Loc.LanguageChanged += RefreshCopyButton;
        Closed += (_, _) => Loc.LanguageChanged -= RefreshCopyButton;
    }

    private void RefreshCopyButton() =>
        CopyCallbackButton.Content = Loc.T(_callbackCopied ? "Setup_Copied" : "Setup_CopyCallback");

    private void CopyCallbackButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(CallbackUri);
            _callbackCopied = true;
            RefreshCopyButton();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            System.Windows.MessageBox.Show(
                Loc.T("Setup_ClipboardError"),
                Loc.T("App_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(DashboardUri) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            System.Windows.MessageBox.Show(
                DashboardUri,
                Loc.T("Setup_BrowserError_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
