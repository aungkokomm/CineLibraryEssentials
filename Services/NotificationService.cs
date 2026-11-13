using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace CineLibraryEssentials.Services;

/// <summary>
/// Windows notifications, for a long batch that ends while the app isn't in
/// front. Clicking one brings the app back. Quietly does nothing where Windows
/// doesn't offer them.
/// </summary>
public static class NotificationService
{
    private static bool _registered;

    /// <summary>Call once at start-up; <paramref name="onClicked"/> runs (off the UI thread) when one is clicked.</summary>
    public static void Register(Action onClicked)
    {
        try
        {
            if (!AppNotificationManager.IsSupported()) return;
            // The click handler has to be in place before Register
            AppNotificationManager.Default.NotificationInvoked += (_, _) => onClicked();
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            DiagnosticsService.Record($"Windows notifications unavailable: {ex.Message}");
        }
    }

    /// <summary>Call when the app closes, so nothing is left registered for an app that's gone.</summary>
    public static void Unregister()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception ex) { Debug.WriteLine($"Notification unregister failed: {ex.Message}"); }
        _registered = false;
    }

    /// <summary>Shows a notification, unless one of this app's windows is the one in front.</summary>
    public static void ShowIfAway(string title, string message)
    {
        if (!_registered || AppIsInFront()) return;
        try
        {
            AppNotificationManager.Default.Show(new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Notification failed: {ex.Message}");
        }
    }

    /// <summary>True when the window in front (main, editor or details) belongs to this app.</summary>
    private static bool AppIsInFront()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
