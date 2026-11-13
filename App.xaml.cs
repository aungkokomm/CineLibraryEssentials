using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using CineLibraryEssentials.Services;
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

        // A finished batch can say so while the app is in the background; a click brings it back
        NotificationService.Register(() => MainWindow.DispatcherQueue.TryEnqueue(BringToFront));
        MainWindow.Closed += (_, _) => NotificationService.Unregister();
    }

    private static void BringToFront()
    {
        if (MainWindow.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        MainWindow.Activate();
    }
}
