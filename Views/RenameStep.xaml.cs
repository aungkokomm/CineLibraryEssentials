using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using CineLibraryEssentials.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace CineLibraryEssentials.Views;

public sealed partial class RenameStep : UserControl
{
    private RenameViewModel? _viewModel;
    private readonly ConfigService _configService = new();

    public RenameStep()
    {
        InitializeComponent();
    }

    public void SetViewModel(RenameViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    // -----------------------------------------------------------------
    //  Folder picker (Browse button)
    // -----------------------------------------------------------------

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
                SourceFolderText.Text = folder.Path;
                await _viewModel.LoadFilesAsync(folder.Path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------
    //  Drag-and-drop folder onto the page (B1)
    // -----------------------------------------------------------------

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop folder to load";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var folder = items.FirstOrDefault(i => i is StorageFolder) as StorageFolder;

            // If they dropped a file, use its parent folder
            if (folder == null)
            {
                var file = items.FirstOrDefault(i => i is StorageFile) as StorageFile;
                if (file != null)
                {
                    var parentPath = System.IO.Path.GetDirectoryName(file.Path);
                    if (!string.IsNullOrEmpty(parentPath) && _viewModel != null)
                    {
                        SourceFolderText.Text = parentPath;
                        await _viewModel.LoadFilesAsync(parentPath);
                    }
                }
                return;
            }

            if (_viewModel != null)
            {
                SourceFolderText.Text = folder.Path;
                await _viewModel.LoadFilesAsync(folder.Path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Drop error: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------
    //  Toolbar buttons
    // -----------------------------------------------------------------

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
        => _viewModel?.SelectAllCommand.Execute(null);

    private void OnSelectNoneClick(object sender, RoutedEventArgs e)
        => _viewModel?.SelectNoneCommand.Execute(null);

    private void OnApplyTitleCaseClick(object sender, RoutedEventArgs e)
        => _viewModel?.ApplyTitleCaseCommand.Execute(null);

    private void OnFindReplaceClick(object sender, RoutedEventArgs e)
        => _viewModel?.FindAndReplaceCommand.Execute(null);

    private void OnResetClick(object sender, RoutedEventArgs e)
        => _viewModel?.ResetCleaningCommand.Execute(null);

    // -----------------------------------------------------------------
    //  Keyboard shortcut: Ctrl+A
    // -----------------------------------------------------------------

    private void OnAcceleratorSelectAll(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _viewModel?.SelectAllCommand.Execute(null);
        args.Handled = true;
    }

    // -----------------------------------------------------------------
    //  TMDb search per row (E1)
    // -----------------------------------------------------------------

    private async void OnTmdbSearchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not FilePreview preview) return;
        if (_viewModel == null) return;

        var apiKey = _configService.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            await ShowMessageAsync("TMDb API key is not configured.");
            return;
        }

        var dialog = new TmdbSearchDialog(apiKey)
        {
            XamlRoot = this.XamlRoot
        };
        dialog.SetInitialQuery(preview.CleanedName);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.SelectedItem != null)
        {
            _viewModel.ApplyTmdbMatch(preview, dialog.SelectedItem.Title, dialog.SelectedItem.Year);
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

    // -----------------------------------------------------------------
    //  Rename in place — destructive op, requires confirmation
    // -----------------------------------------------------------------

    private async void OnRenameInPlaceClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var pending = _viewModel.PendingRenameCount;
        if (pending == 0)
        {
            await ShowMessageAsync("No files need renaming. (Cleaned name matches the current file name for every selected row.)");
            return;
        }

        // Confirm
        var confirm = new ContentDialog
        {
            Title = "Rename files?",
            Content = $"This will rename {pending} file(s) on disk in their current folder. " +
                      $"Matching subtitle files (.srt, .sub, etc.) will also be renamed.\n\n" +
                      $"This action cannot be undone automatically.",
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var opResult = await _viewModel.RenameInPlaceAsync();

        // Report
        if (opResult.Success && opResult.Errors.Count == 0)
        {
            await ShowMessageAsync($"✓ Renamed {pending} file(s) successfully.");
        }
        else
        {
            var msg = $"Done with {opResult.Errors.Count} error(s):\n\n" +
                      string.Join("\n", opResult.Errors.Take(10));
            if (opResult.Errors.Count > 10)
                msg += $"\n... and {opResult.Errors.Count - 10} more.";
            await ShowMessageAsync(msg);
        }
    }
}
