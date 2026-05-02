using CineLibraryEssentials.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CineLibraryEssentials.Views;

public sealed partial class FileToFolderStep : UserControl
{
    private FileToFolderViewModel? _viewModel;

    /// <summary>Raised after a successful "Run File to Folder" so the wizard can advance.</summary>
    public event EventHandler? OperationCompleted;

    public FileToFolderStep()
    {
        InitializeComponent();
    }

    public void SetViewModel(FileToFolderViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>Called by the wizard when entering Step 2 — populates the list automatically.</summary>
    public void RefreshFromRenameStep()
    {
        _viewModel?.RefreshFromRenameStep();
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null && _viewModel != null)
            {
                _viewModel.OutputFolderPath = folder.Path;
                // OutputFolderPath setter triggers LoadPreview to refresh destinations
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
        }
    }

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        if (string.IsNullOrEmpty(_viewModel.OutputFolderPath))
        {
            await ShowMessageAsync("Please pick an output folder first.");
            return;
        }

        var anyChecked = _viewModel.OperationsPreview.Any(op => op.IsSelected);
        if (!anyChecked)
        {
            await ShowMessageAsync("No files selected. Tick at least one row to continue.");
            return;
        }

        var result = await _viewModel.RunAsync();
        if (result.Success && result.Errors.Count == 0)
        {
            // Auto-advance to Step 3
            OperationCompleted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            var msg = $"Done with {result.Errors.Count} error(s):\n\n" +
                      string.Join("\n", result.Errors.Take(10));
            if (result.Errors.Count > 10)
                msg += $"\n... and {result.Errors.Count - 10} more.";
            await ShowMessageAsync(msg);
        }
    }

    private async Task ShowMessageAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "CineLibrary",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
