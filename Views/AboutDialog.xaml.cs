using Microsoft.UI.Xaml.Controls;

namespace CineLibraryEssentials.Views;

public sealed partial class AboutDialog : ContentDialog
{
    public AboutDialog()
    {
        InitializeComponent();

        // Surface the actual assembly version
        var version = typeof(AboutDialog).Assembly.GetName().Version;
        if (version != null)
            VersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";
    }
}
