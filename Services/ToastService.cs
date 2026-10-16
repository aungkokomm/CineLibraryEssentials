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

    // Warnings and errors also go to "Copy diagnostics" (About)
    public static void Warning(string message, string? title = null, int autoDismissMs = 6000)
    {
        DiagnosticsService.Record(message);
        Show(message, title, InfoBarSeverity.Warning, autoDismissMs);
    }

    public static void Error(string message, string? title = null, int autoDismissMs = 7000)
    {
        DiagnosticsService.Record(message);
        Show(message, title, InfoBarSeverity.Error, autoDismissMs);
    }

    /// <summary>
    /// Shows a toast with an action button. Returns the InfoBar when called on the
    /// UI thread (the usual case) so callers can update it; the button is wired up
    /// either way.
    /// </summary>
    public static InfoBar? ShowAction(string message, string? title, string actionText, Action onAction,
        int autoDismissMs = 30000, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        return Show(message, title, severity, autoDismissMs, configure: bar =>
        {
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
        });
    }

    private static InfoBar? Show(string message, string? title, InfoBarSeverity severity, int autoDismissMs,
        Action<InfoBar>? configure = null)
    {
        if (_host == null || _dispatcherQueue == null) return null;

        InfoBar? created = null;
        void Build()
        {
            var bar = new InfoBar
            {
                Title = title ?? string.Empty,
                Message = message,
                Severity = severity,
                IsOpen = true,
                IsClosable = true,
                BorderThickness = new Microsoft.UI.Xaml.Thickness(0),   // the card draws the border
            };
            configure?.Invoke(bar);

            // InfoBar's Info / Success backgrounds are see-through (they're made to
            // sit inside a page), so when floating over the list the rows showed
            // through. Put each one on a solid card with a border and shadow.
            var card = new Border
            {
                Child = bar,
                Background = Brush("SolidBackgroundFillColorBaseBrush"),
                BorderBrush = Brush("SurfaceStrokeColorFlyoutBrush"),
                BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
                CornerRadius = new Microsoft.UI.Xaml.CornerRadius(8),
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 8),
                Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow(),
                Translation = new System.Numerics.Vector3(0, 0, 32),
            };

            // Newest on top
            _host.Children.Insert(0, card);
            bar.Closed += (_, _) =>
            {
                if (_host.Children.Contains(card))
                    _host.Children.Remove(card);
            };

            _ = AutoCloseAsync(bar, autoDismissMs);
            created = bar;
        }

        // Build right away when already on the UI thread. TryEnqueue would run it
        // later, after this method returned null, which is why action buttons
        // (Undo, Download and install, Don't show again) never appeared.
        if (_dispatcherQueue.HasThreadAccess) Build();
        else _dispatcherQueue.TryEnqueue(Build);
        return created;
    }

    // Theme brush lookup by key; the indexer searches the merged theme dictionaries.
    private static Microsoft.UI.Xaml.Media.Brush? Brush(string key)
    {
        try { return Microsoft.UI.Xaml.Application.Current.Resources[key] as Microsoft.UI.Xaml.Media.Brush; }
        catch { return null; }
    }

    private static async Task AutoCloseAsync(InfoBar bar, int delayMs)
    {
        await Task.Delay(delayMs);
        _dispatcherQueue?.TryEnqueue(() => { try { bar.IsOpen = false; } catch { } });
    }
}
