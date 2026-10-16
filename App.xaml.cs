using Microsoft.UI.Xaml;
using CineLibraryEssentials.Views;

namespace CineLibraryEssentials;

public partial class App : Application
{
    public static Window MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();

        // Non-fatal background errors, remembered for "Copy diagnostics" in About
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Services.DiagnosticsService.Record($"Background error: {e.Exception.GetBaseException().Message}");
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
