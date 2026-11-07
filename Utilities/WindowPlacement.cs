using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace CineLibraryEssentials.Utilities;

/// <summary>
/// Opens a window (details, editor) where and how big the user last left it,
/// without a setting: the size and position are saved when it closes.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// Puts the window back where it was last closed. False when nothing is saved
    /// yet or that spot is no longer on any screen, so the caller places it.
    /// </summary>
    public static bool Restore(Window window, string name)
    {
        if (new ConfigService().GetWindowPlacement(name) is not { } b || b.Width < 400 || b.Height < 300)
            return false;
        try
        {
            var rect = new RectInt32(b.X, b.Y, b.Width, b.Height);
            if (DisplayArea.GetFromRect(rect, DisplayAreaFallback.None) is not { } display)
                return false;   // that monitor is gone
            var work = display.WorkArea;
            rect.Width = Math.Min(rect.Width, work.Width);
            rect.Height = Math.Min(rect.Height, work.Height);
            window.AppWindow.MoveAndResize(rect);
            if (b.Maximized && window.AppWindow.Presenter is OverlappedPresenter p)
                p.Maximize();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Saves the window's size and position when it closes. Call after placing it.</summary>
    public static void Remember(Window window, string name)
    {
        // Start from the saved bounds, so a window that opens maximized keeps its restored size
        var saved = new ConfigService().GetWindowPlacement(name);
        var last = saved == null ? new WindowBounds()
            : new WindowBounds { X = saved.X, Y = saved.Y, Width = saved.Width, Height = saved.Height };
        void Capture()
        {
            try
            {
                var state = (window.AppWindow.Presenter as OverlappedPresenter)?.State;
                if (state == OverlappedPresenterState.Minimized) return;
                if (state == OverlappedPresenterState.Maximized)
                {
                    last.Maximized = true;   // keep the restored size from before
                    return;
                }
                var pos = window.AppWindow.Position;
                var size = window.AppWindow.Size;
                last = new WindowBounds { X = pos.X, Y = pos.Y, Width = size.Width, Height = size.Height };
            }
            catch { /* the window is going away */ }
        }

        Capture();
        window.AppWindow.Changed += (_, e) =>
        {
            if (e.DidPositionChange || e.DidSizeChange || e.DidPresenterChange) Capture();
        };
        window.Closed += (_, _) =>
        {
            if (last.Width > 0) new ConfigService().SetWindowPlacement(name, last);
        };
    }
}
