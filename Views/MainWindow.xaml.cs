using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;

namespace CineLibraryEssentials.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ConfigureTitleBar();
        SetWindowIcon();
        ContentFrame.Navigate(typeof(WizardPage));
    }

    private void ConfigureTitleBar()
    {
        // Extend content into the title bar so our custom AppTitleBar shows
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void SetWindowIcon()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetIcon failed: {ex.Message}");
        }
    }
}
