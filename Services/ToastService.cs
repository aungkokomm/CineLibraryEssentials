using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace CineLibraryEssentials.Services;

/// <summary>
/// Centralised toast notifications. The MainWindow registers a host (the panel that
/// renders InfoBars at the bottom of the window) and any page can call <see cref="Show"/>
/// to display a transient message that auto-dismisses after a few seconds.
/// </summary>
public static class ToastService
{
    private static Panel? _host;
    private static DispatcherQueue? _dispatcherQueue;

    public static void Register(Panel host, DispatcherQueue dispatcherQueue)
    {
        _host = host;
        _dispatcherQueue = dispatcherQueue;
    }

    public static void Info(string message, string? title = null, int autoDismissMs = 4000)
        => Show(message, title, InfoBarSeverity.Informational, autoDismissMs);

    public static void Success(string message, string? title = null, int autoDismissMs = 3500)
        => Show(message, title, InfoBarSeverity.Success, autoDismissMs);

    public static void Warning(string message, string? title = null, int autoDismissMs = 6000)
        => Show(message, title, InfoBarSeverity.Warning, autoDismissMs);

    public static void Error(string message, string? title = null, int autoDismissMs = 7000)
        => Show(message, title, InfoBarSeverity.Error, autoDismissMs);

    /// <summary>Shows an action toast — caller wires up the action via the returned InfoBar.</summary>
    public static InfoBar? ShowAction(string message, string? title, string actionText, Action onAction, int autoDismissMs = 30000)
    {
        var bar = Show(message, title, InfoBarSeverity.Informational, autoDismissMs, autoClose: false);
        if (bar == null) return null;

        var btn = new Button
        {
            Content = actionText,
            Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 8, 0)
        };
        btn.Click += (_, _) =>
        {
            onAction();
            bar.IsOpen = false;
        };
        bar.ActionButton = btn;
        bar.IsClosable = true;

        // schedule auto-dismiss
        _ = AutoCloseAsync(bar, autoDismissMs);

        return bar;
    }

    private static InfoBar? Show(string message, string? title, InfoBarSeverity severity, int autoDismissMs, bool autoClose = true)
    {
        if (_host == null || _dispatcherQueue == null) return null;

        InfoBar? bar = null;
        _dispatcherQueue.TryEnqueue(() =>
        {
            bar = new InfoBar
            {
                Title = title ?? string.Empty,
                Message = message,
                Severity = severity,
                IsOpen = true,
                IsClosable = true,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 6)
            };
            // Newest on top
            _host.Children.Insert(0, bar);
            bar.Closed += (s, _) =>
            {
                if (s is InfoBar b && _host!.Children.Contains(b))
                    _host.Children.Remove(b);
            };

            if (autoClose) _ = AutoCloseAsync(bar, autoDismissMs);
        });
        return bar;
    }

    private static async Task AutoCloseAsync(InfoBar bar, int delayMs)
    {
        await Task.Delay(delayMs);
        _dispatcherQueue?.TryEnqueue(() => { try { bar.IsOpen = false; } catch { } });
    }
}
