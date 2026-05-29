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
        LaunchMaximized();

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
            // Respect the user's "Check for updates on startup" preference.
            if (!_configService.GetAutoCheckForUpdates()) return;

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
        // If the release ships a Setup .exe asset, the Download button does the
        // full in-app flow: fetch → launch installer → exit. If no asset is found
        // (rare; manual GitHub release without attached binary), fall back to
        // opening the release page in the user's browser.
        var hasInstallerAsset = !string.IsNullOrEmpty(info.InstallerUrl);

        ToastService.ShowAction(
            message: hasInstallerAsset
                ? $"Version {info.LatestVersion} is available — you're on {info.CurrentVersion}."
                : $"Version {info.LatestVersion} is available — open the release page to download.",
            title: "Update available",
            actionText: hasInstallerAsset ? "Download and install" : "Open release page",
            onAction: () =>
            {
                if (hasInstallerAsset)
                    _ = DownloadAndInstallAsync(info);
                else
                    OpenReleasePage(info.ReleaseUrl);
            },
            autoDismissMs: 60000);
    }

    /// <summary>
    /// Downloads the release installer to %TEMP%, shows a progress toast while
    /// it runs, then launches the installer and exits the app so the new build
    /// can take over.
    /// </summary>
    private async Task DownloadAndInstallAsync(UpdateService.UpdateInfo info)
    {
        var progressToast = ToastService.ShowAction(
            message: "Starting download — 0%",
            title: $"Downloading v{info.LatestVersion}",
            actionText: "Cancel",
            onAction: () => { /* set by cancel token below */ },
            autoDismissMs: int.MaxValue);

        var cts = new CancellationTokenSource();
        if (progressToast?.ActionButton is Microsoft.UI.Xaml.Controls.Button cancelBtn)
        {
            cancelBtn.Click += (_, _) => cts.Cancel();
        }

        var progress = new Progress<double>(pct =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (progressToast != null)
                    progressToast.Message = $"Downloading… {pct * 100:F0}%";
            });
        });

        try
        {
            var installerPath = await _updateService.DownloadInstallerAsync(info, progress, cts.Token);

            DispatcherQueue.TryEnqueue(() =>
            {
                if (progressToast != null)
                {
                    progressToast.Message = "Launching installer…";
                    progressToast.IsOpen = false;
                }
            });

            _updateService.LaunchInstallerAndExit(installerPath);
        }
        catch (OperationCanceledException)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (progressToast != null) progressToast.IsOpen = false;
                ToastService.Info("Update download cancelled.");
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update download failed: {ex.Message}");
            DispatcherQueue.TryEnqueue(() =>
            {
                if (progressToast != null) progressToast.IsOpen = false;
                ToastService.Error($"Update failed: {ex.Message}");
            });
        }
    }

    private static void OpenReleasePage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Open release URL failed: {ex.Message}");
        }
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    /// <summary>
    /// Opens the window maximized. The restored bounds above still apply as the
    /// "restore down" size, so un-maximizing returns to a sensible windowed size.
    /// </summary>
    private void LaunchMaximized()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Maximize failed: {ex.Message}");
        }
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
