using CineLibraryEssentials.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace CineLibraryEssentials.Views;

public sealed partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();

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
