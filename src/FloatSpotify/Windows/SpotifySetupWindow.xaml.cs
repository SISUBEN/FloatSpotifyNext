using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace FloatSpotify.Windows;

public partial class SpotifySetupWindow : Window
{
    private const string CallbackUri = "http://127.0.0.1:8888/callback";
    private const string DashboardUri = "https://developer.spotify.com/dashboard";

    public SpotifySetupWindow()
    {
        InitializeComponent();
    }

    private void CopyCallbackButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(CallbackUri);
            CopyCallbackButton.Content = "已复制";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            System.Windows.MessageBox.Show(
                "无法访问剪贴板，请手动复制回调地址。",
                "FloatSpotify",
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
                "无法打开浏览器",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
