using System.Diagnostics;
using CineLibraryEssentials.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace CineLibraryEssentials.Views;

public sealed partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly UpdateService _updateService = new();

    public MainWindow()
    {
        InitializeComponent();
        ConfigureTitleBar();
        SetWindowIcon();
        RestoreWindowBounds();
        WireWindowPersistence();

        // Register the toast host so any page can call ToastService.Info/Success/...
        ToastService.Register(ToastHost, DispatcherQueue);

        ContentFrame.Navigate(typeof(WizardPage));

        // Fire-and-forget auto-update check — runs once per 24h, silent if up-to-date.
        _ = AutoCheckForUpdatesAsync();
    }

    /// <summary>
    /// Silently polls GitHub on startup and, if a newer release exists, shows a
    /// dismissable toast with a "Download" button. Throttled to once per 24h
    /// and respects the user's "Skip this version" choice so we never nag.
    /// </summary>
    private async Task AutoCheckForUpdatesAsync()
    {
        try
        {
            // Throttle: only check once every 24 hours so launches don't hit the
            // GitHub API every time.
            var lastCheck = _configService.GetLastUpdateCheck();
            if (lastCheck != DateTime.MinValue
                && (DateTime.UtcNow - lastCheck) < TimeSpan.FromHours(24))
                return;

            // Wait ~5s after launch so the toast doesn't slam the user the instant
            // the window appears.
            await Task.Delay(TimeSpan.FromSeconds(5));

            var info = await _updateService.CheckForUpdateAsync();
            // Always update the timestamp so failures don't retry on every launch either.
            _configService.SetLastUpdateCheckNow();

            if (!info.Success || !info.IsUpdateAvailable) return;

            // Honour the user's "Skip" — only nag again once a newer version ships.
            var skipped = _configService.GetSkippedUpdateVersion();
            if (!string.IsNullOrEmpty(skipped)
                && string.Equals(skipped, info.LatestVersion, StringComparison.Ordinal))
                return;

            // Show the toast on the UI thread.
            DispatcherQueue.TryEnqueue(() => ShowUpdateToast(info));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AutoCheckForUpdates failed: {ex.Message}");
        }
    }

    private void ShowUpdateToast(UpdateService.UpdateInfo info)
    {
        // Action toast: primary button opens the release page; the toast's built-in
        // X dismisses for this session. The 24h throttle means it won't reappear
        // again today even if the user just closes it.
        ToastService.ShowAction(
            message: $"Version {info.LatestVersion} is available — you're on {info.CurrentVersion}.",
            title: "Update available",
            actionText: "Download",
            onAction: () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = info.ReleaseUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Open release URL failed: {ex.Message}");
                }
            },
            autoDismissMs: 60000); // visible for one minute, then auto-fades
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void SetWindowIcon()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(iconPath))
                AppWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetIcon failed: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------
    //  Window-size persistence
    // -----------------------------------------------------------------

    private void RestoreWindowBounds()
    {
        try
        {
            var (w, h, x, y) = _configService.GetWindowBounds();
            if (w < 600) w = 1200;
            if (h < 400) h = 800;

            if (x >= 0 && y >= 0)
                AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
            else
                AppWindow.Resize(new SizeInt32(w, h));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Restore bounds failed: {ex.Message}");
        }
    }

    private void WireWindowPersistence()
    {
        AppWindow.Changed += (s, e) =>
        {
            // Save on resize / move
            if (e.DidPositionChange || e.DidSizeChange)
            {
                try
                {
                    var size = AppWindow.Size;
                    var pos = AppWindow.Position;
                    _configService.SetWindowBounds(size.Width, size.Height, pos.X, pos.Y);
                }
                catch { }
            }
        };
    }
}
