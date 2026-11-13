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

    /// <summary>Raised after Undo put Step 2's moves back, so Step 3 can drop the cards that went with them.</summary>
    public event EventHandler? Undone;

    /// <summary>Raised when the footer "← Back" is clicked.</summary>
    public event EventHandler? BackRequested;

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    public FileToFolderStep()
    {
        InitializeComponent();
    }

    public void SetViewModel(FileToFolderViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.OperationsPreview.CollectionChanged += (_, _) =>
        {
            UpdateEmptyState();
            SyncSelectAllCheckBox();
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileToFolderViewModel.IsAllSelected)
                || e.PropertyName == nameof(FileToFolderViewModel.IsNoneSelected))
            {
                SyncSelectAllCheckBox();
            }
        };
        UpdateEmptyState();
        SyncSelectAllCheckBox();
    }

    private void SyncSelectAllCheckBox()
    {
        if (_viewModel == null) return;
        // Tri-state: all → checked, none → unchecked, partial → indeterminate.
        SelectAllCheckBox.IsChecked = _viewModel.IsAllSelected
            ? true
            : _viewModel.IsNoneSelected
                ? false
                : (bool?)null;
    }

    private void OnSelectAllToggle(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if (_viewModel.IsAllSelected)
            _viewModel.SelectNoneCommand.Execute(null);
        else
            _viewModel.SelectAllCommand.Execute(null);
        SyncSelectAllCheckBox();
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

    private async void OnRunClick(object sender, RoutedEventArgs e) => await RunAsync();

    /// <summary>
    /// Run File to Folder: moves the ticked rows, offers Undo, and on success moves
    /// on to Step 3. True when everything moved (also used by Step 1's "Clean,
    /// organize and scrape").
    /// </summary>
    public async Task<bool> RunAsync()
    {
        if (_viewModel == null) return false;

        var anyChecked = _viewModel.OperationsPreview.Any(op => op.IsSelected);
        if (!anyChecked)
        {
            ToastService.Warning("No files selected. Tick at least one row to continue.");
            return false;
        }

        var result = await _viewModel.RunAsync();
        if (result.Success && result.Errors.Count == 0)
        {
            var organized = $"Organized {_viewModel.OperationsPreview.Count(op => op.IsSelected)} file(s).";
            if (result.Moves.Count > 0) OfferUndo(result, organized);
            else ToastService.Success(organized);
            OperationCompleted?.Invoke(this, EventArgs.Empty);
            return true;
        }

        var top = string.Join("\n", result.Errors.Take(5));
        var more = result.Errors.Count > 5 ? $"\n... and {result.Errors.Count - 5} more." : "";
        ToastService.Error($"{result.Errors.Count} error(s):\n{top}{more}");
        if (result.Moves.Count > 0) OfferUndo(result, "The other files were moved.");
        return false;
    }

    /// <summary>For 30 seconds, Undo puts everything Step 2 just moved back where it was.</summary>
    private void OfferUndo(ProcessingResult result, string message)
    {
        ToastService.ShowAction(
            message: message,
            title: "Organize",
            actionText: "Undo",
            onAction: () =>
            {
                var (back, notBack, kept) = FileToFolderService.Undo(result);
                var text = $"Moved {back} item{(back == 1 ? "" : "s")} back where {(back == 1 ? "it was" : "they were")}."
                    + (notBack > 0 ? $" {notBack} couldn't be moved back: they were moved or renamed since." : "")
                    + (kept > 0 ? $" Kept {kept} new folder{(kept == 1 ? "" : "s")} that now hold other files." : "");
                if (notBack > 0) ToastService.Warning(text);
                else ToastService.Success(text);
                Undone?.Invoke(this, EventArgs.Empty);
            },
            severity: InfoBarSeverity.Success);
    }
}
