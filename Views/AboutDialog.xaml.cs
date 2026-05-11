using System.Diagnostics;
using CineLibraryEssentials.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CineLibraryEssentials.Views;

public sealed partial class AboutDialog : ContentDialog
{
    private readonly UpdateService _updateService = new();

    public AboutDialog()
    {
        InitializeComponent();

        // Surface the actual assembly version
        var version = typeof(AboutDialog).Assembly.GetName().Version;
        if (version != null)
            VersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        // Disable the button + show spinner while we hit GitHub.
        CheckUpdatesButton.IsEnabled = false;
        UpdateProgress.IsActive = true;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateStatusText.Visibility = Visibility.Collapsed;

        try
        {
            var info = await _updateService.CheckForUpdateAsync();

            if (!info.Success)
            {
                UpdateStatusText.Text = $"⚠ Couldn't check: {info.Error ?? "unknown error"}.";
                UpdateStatusText.Visibility = Visibility.Visible;
                return;
            }

            if (info.IsUpdateAvailable)
            {
                UpdateStatusText.Text =
                    $"⬆ Version {info.LatestVersion} is available (you have {info.CurrentVersion}). " +
                    $"Click here to open the download page.";
                UpdateStatusText.Visibility = Visibility.Visible;
                // Make the status text clickable -> opens GitHub releases page.
                UpdateStatusText.Tapped -= OnReleaseLinkTapped;
                UpdateStatusText.Tapped += OnReleaseLinkTapped;
                UpdateStatusText.Tag = info.ReleaseUrl;
            }
            else
            {
                UpdateStatusText.Text = $"✓ You're on the latest version ({info.CurrentVersion}).";
                UpdateStatusText.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            UpdateProgress.IsActive = false;
            UpdateProgress.Visibility = Visibility.Collapsed;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void OnReleaseLinkTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is TextBlock tb && tb.Tag is string url && !string.IsNullOrEmpty(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { /* user can copy URL manually from the message */ }
        }
    }
}
