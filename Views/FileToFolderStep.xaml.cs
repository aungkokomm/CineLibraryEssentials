using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using CineLibraryEssentials.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace CineLibraryEssentials.Views;

public sealed partial class FileToFolderStep : UserControl
{
    private FileToFolderViewModel? _viewModel;
    private readonly ConfigService _configService = new();

    /// <summary>Raised after a successful "Run File to Folder" so the wizard can advance.</summary>
    public event EventHandler? OperationCompleted;

    public FileToFolderStep()
    {
        InitializeComponent();
        Loaded += (_, _) => RebuildRecentOutputFlyout();
    }

    public void SetViewModel(FileToFolderViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.OperationsPreview.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        if (_viewModel == null) return;
        EmptyState.Visibility = _viewModel.OperationsPreview.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>Called by the wizard when entering Step 2 — populates the list automatically.</summary>
    public void RefreshFromRenameStep()
    {
        _viewModel?.RefreshFromRenameStep();
        UpdateEmptyState();
    }

    // -----------------------------------------------------------------
    //  Output folder picker + recent folders
    // -----------------------------------------------------------------

    private async void OnBrowseClick(SplitButton sender, SplitButtonClickEventArgs args)
    {
        try
        {
            var picker = new FolderPicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null && _viewModel != null)
            {
                _viewModel.OutputFolderPath = folder.Path;
                RebuildRecentOutputFlyout();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
        }
    }

    private void RebuildRecentOutputFlyout()
    {
        RecentOutputFlyout.Items.Clear();
        var recents = _configService.GetRecentOutputFolders();
        if (recents.Count == 0)
        {
            RecentOutputFlyout.Items.Add(new MenuFlyoutItem { Text = "(no recent folders)", IsEnabled = false });
            return;
        }
        foreach (var path in recents)
        {
            var mfi = new MenuFlyoutItem { Text = path };
            mfi.Click += (_, _) =>
            {
                if (_viewModel != null && Directory.Exists(path))
                {
                    _viewModel.OutputFolderPath = path;
                    RebuildRecentOutputFlyout();
                }
                else if (!Directory.Exists(path))
                {
                    ToastService.Warning($"Folder no longer exists: {path}");
                }
            };
            RecentOutputFlyout.Items.Add(mfi);
        }
    }

    // -----------------------------------------------------------------
    //  Manual Add buttons
    // -----------------------------------------------------------------

    private async void OnAddFilesClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        try
        {
            var picker = new FileOpenPicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            foreach (var ext in Utilities.FileFormatValidator.GetSupportedFormats())
                picker.FileTypeFilter.Add(ext);

            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0)
            {
                _viewModel.AddFiles(files.Select(f => f.Path));
                ToastService.Success($"Added {files.Count} file(s).");
            }
        }
        catch (Exception ex)
        {
            ToastService.Error($"Add files failed: {ex.Message}");
        }
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        try
        {
            var picker = new FolderPicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                var beforeCount = _viewModel.OperationsPreview.Count;
                _viewModel.AddFolder(folder.Path, recursive: true);
                var added = _viewModel.OperationsPreview.Count - beforeCount;
                ToastService.Success($"Added {added} file(s) from {Path.GetFileName(folder.Path)}.");
            }
        }
        catch (Exception ex)
        {
            ToastService.Error($"Add folder failed: {ex.Message}");
        }
    }

    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.ClearAll();
    }

    // -----------------------------------------------------------------
    //  Drag-drop
    // -----------------------------------------------------------------

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            DragOverlay.Visibility = Visibility.Visible;
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DragOverlay.Visibility = Visibility.Collapsed;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
        if (_viewModel == null) return;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var folders = items.OfType<StorageFolder>().ToList();
            var files = items.OfType<StorageFile>().Select(f => f.Path).ToList();

            int totalBefore = _viewModel.OperationsPreview.Count;
            foreach (var f in folders) _viewModel.AddFolder(f.Path, recursive: true);
            if (files.Count > 0) _viewModel.AddFiles(files);
            int added = _viewModel.OperationsPreview.Count - totalBefore;
            if (added > 0) ToastService.Success($"Added {added} file(s).");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Drop error: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------
    //  Right-click context menu
    // -----------------------------------------------------------------

    private FileOperation? GetContextRow(object sender)
    {
        if (sender is MenuFlyoutItem mfi && mfi.DataContext is FileOperation op) return op;
        return null;
    }

    private void OnContextOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var op = GetContextRow(sender);
        if (op == null || string.IsNullOrEmpty(op.OriginalFilePath)) return;
        var dir = Path.GetDirectoryName(op.OriginalFilePath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private void OnContextRemoveClick(object sender, RoutedEventArgs e)
    {
        var op = GetContextRow(sender);
        if (op != null) _viewModel?.RemoveOperation(op);
    }

    // -----------------------------------------------------------------
    //  Run
    // -----------------------------------------------------------------

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        if (string.IsNullOrEmpty(_viewModel.OutputFolderPath))
        {
            ToastService.Warning("Please pick an output folder first.");
            return;
        }

        var anyChecked = _viewModel.OperationsPreview.Any(op => op.IsSelected);
        if (!anyChecked)
        {
            ToastService.Warning("No files selected. Tick at least one row to continue.");
            return;
        }

        var result = await _viewModel.RunAsync();
        if (result.Success && result.Errors.Count == 0)
        {
            ToastService.Success($"Organized {_viewModel.OperationsPreview.Count(op => op.IsSelected)} file(s).");
            OperationCompleted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            var top = string.Join("\n", result.Errors.Take(5));
            var more = result.Errors.Count > 5 ? $"\n... and {result.Errors.Count - 5} more." : "";
            ToastService.Error($"{result.Errors.Count} error(s):\n{top}{more}");
        }
    }
}
