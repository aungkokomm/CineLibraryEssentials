using Microsoft.UI.Xaml;
using CineLibraryEssentials.Views;

namespace CineLibraryEssentials;

public partial class App : Application
{
    public static Window MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
