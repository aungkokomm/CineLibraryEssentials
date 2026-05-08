using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using CineLibraryEssentials.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace CineLibraryEssentials.Views;

public sealed partial class RenameStep : UserControl
{
    private RenameViewModel? _viewModel;
    private readonly ConfigService _configService = new();
    private const string WarningId = "step1.rename-warning";

    public RenameStep()
    {
        InitializeComponent();

        // Hide the warning if user has previously dismissed it
        if (_configService.IsWarningDismissed(WarningId))
            WarningInfoBar.IsOpen = false;

        Loaded += (_, _) => RebuildRecentFoldersFlyout();
    }

    public void SetViewModel(RenameViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RenameViewModel.SortColumn)
                || e.PropertyName == nameof(RenameViewModel.SortDescending))
            {
                UpdateSortIndicators();
            }
        };
        UpdateSortIndicators();
    }

    private void OnWarningInfoBarClosed(InfoBar sender, object args)
    {
        // Permanently dismiss when user explicitly closes
        _configService.DismissWarning(WarningId);
    }

    // -----------------------------------------------------------------
    //  Folder picker (Browse button) + Recent folders flyout
    // -----------------------------------------------------------------

    private async void OnBrowseClick(SplitButton sender, SplitButtonClickEventArgs args)
    {
        await PickAndLoadFolderAsync();
    }

    private async Task PickAndLoadFolderAsync()
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
                RebuildRecentFoldersFlyout();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
        }
    }

    private void RebuildRecentFoldersFlyout()
    {
        RecentFoldersFlyout.Items.Clear();
        var recents = _configService.GetRecentSourceFolders();
        if (recents.Count == 0)
        {
            var none = new MenuFlyoutItem { Text = "(no recent folders)", IsEnabled = false };
            RecentFoldersFlyout.Items.Add(none);
            return;
        }
        foreach (var path in recents)
        {
            var mfi = new MenuFlyoutItem
            {
                Text = path,
                Icon = new FontIcon { Glyph = "" }
            };
            mfi.Click += async (_, _) =>
            {
                if (_viewModel != null && Directory.Exists(path))
                {
                    SourceFolderText.Text = path;
                    await _viewModel.LoadFilesAsync(path);
                    RebuildRecentFoldersFlyout();
                }
                else if (!Directory.Exists(path))
                {
                    ToastService.Warning($"Folder no longer exists: {path}");
                }
            };
            RecentFoldersFlyout.Items.Add(mfi);
        }
    }

    // -----------------------------------------------------------------
    //  Drag-and-drop folder onto the page
    // -----------------------------------------------------------------

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop to load folder";
            e.DragUIOverride.IsCaptionVisible = true;
            DragOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var folder = items.FirstOrDefault(i => i is StorageFolder) as StorageFolder;

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
                        RebuildRecentFoldersFlyout();
                    }
                }
                return;
            }

            if (_viewModel != null)
            {
                SourceFolderText.Text = folder.Path;
                await _viewModel.LoadFilesAsync(folder.Path);
                RebuildRecentFoldersFlyout();
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

    private void OnSelectAllClick(object sender, RoutedEventArgs e) => _viewModel?.SelectAllCommand.Execute(null);
    private void OnSelectNoneClick(object sender, RoutedEventArgs e) => _viewModel?.SelectNoneCommand.Execute(null);
    private void OnApplyTitleCaseClick(object sender, RoutedEventArgs e) => _viewModel?.ApplyTitleCaseCommand.Execute(null);
    private void OnFindReplaceClick(object sender, RoutedEventArgs e) => _viewModel?.FindAndReplaceCommand.Execute(null);
    private void OnResetClick(object sender, RoutedEventArgs e) => _viewModel?.ResetCleaningCommand.Execute(null);

    // -----------------------------------------------------------------
    //  Sort header buttons
    // -----------------------------------------------------------------

    private void OnSortOriginalClick(object sender, RoutedEventArgs e) => _viewModel?.ToggleSort("Original");
    private void OnSortCleanedClick(object sender, RoutedEventArgs e) => _viewModel?.ToggleSort("Cleaned");
    private void OnSortConfidenceClick(object sender, RoutedEventArgs e) => _viewModel?.ToggleSort("Confidence");

    private void UpdateSortIndicators()
    {
        if (_viewModel == null) return;
        // up arrow = E74A, down arrow = E74B
        var asc = ""; var desc = "";
        SortOriginalIcon.Glyph = "";
        SortCleanedIcon.Glyph = "";
        SortConfidenceIcon.Glyph = "";

        var glyph = _viewModel.SortDescending ? desc : asc;
        switch (_viewModel.SortColumn)
        {
            case "Original": SortOriginalIcon.Glyph = glyph; break;
            case "Cleaned": SortCleanedIcon.Glyph = glyph; break;
            case "Confidence": SortConfidenceIcon.Glyph = glyph; break;
        }
    }

    // -----------------------------------------------------------------
    //  Keyboard shortcut: Ctrl+A
    // -----------------------------------------------------------------

    private void OnAcceleratorSelectAll(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _viewModel?.SelectAllCommand.Execute(null);
        args.Handled = true;
    }

    // -----------------------------------------------------------------
    //  TMDb search per row
    // -----------------------------------------------------------------

    private async void OnTmdbSearchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not FilePreview preview) return;
        await OpenTmdbDialogForAsync(preview);
    }

    private async Task OpenTmdbDialogForAsync(FilePreview preview)
    {
        if (_viewModel == null) return;

        var apiKey = _configService.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            ToastService.Warning("TMDb API key is not configured.");
            return;
        }

        var dialog = new TmdbSearchDialog(apiKey) { XamlRoot = this.XamlRoot };
        dialog.SetInitialQuery(preview.CleanedName);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.SelectedItem != null)
        {
            _viewModel.ApplyTmdbMatch(preview, dialog.SelectedItem.Title, dialog.SelectedItem.Year);
            ToastService.Success($"Applied TMDb match: {dialog.SelectedItem.TitleWithYear}");
        }
    }

    // -----------------------------------------------------------------
    //  Right-click context menu items
    // -----------------------------------------------------------------

    private FilePreview? GetContextRow(object sender)
    {
        if (sender is MenuFlyoutItem mfi && mfi.DataContext is FilePreview p) return p;
        // Walk up to find the data context
        if (sender is FrameworkElement fe && fe.DataContext is FilePreview pp) return pp;
        return null;
    }

    private async void OnContextSearchTmdbClick(object sender, RoutedEventArgs e)
    {
        var p = GetContextRow(sender);
        if (p != null) await OpenTmdbDialogForAsync(p);
    }

    private void OnContextTitleCaseClick(object sender, RoutedEventArgs e)
    {
        var p = GetContextRow(sender);
        if (p == null) return;
        var ext = System.IO.Path.GetExtension(p.CleanedName);
        var nameOnly = System.IO.Path.GetFileNameWithoutExtension(p.CleanedName);
        p.CleanedName = Utilities.RegexPatterns.SmartTitleCase(nameOnly) + ext;
    }

    private void OnContextResetClick(object sender, RoutedEventArgs e)
    {
        var p = GetContextRow(sender);
        if (p == null) return;
        var parsed = Utilities.RegexPatterns.ParseFilename(p.OriginalName);
        var ext = System.IO.Path.GetExtension(p.OriginalName);
        p.CleanedName = Utilities.PathSanitizer.CreateMovieFileName(parsed.Title, parsed.Year, ext);
        p.Confidence = parsed.Confidence;
        p.Year = parsed.Year;
        p.IsReviewed = false;
    }

    private void OnContextOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var p = GetContextRow(sender);
        if (p == null || string.IsNullOrEmpty(p.OriginalFilePath)) return;
        var dir = System.IO.Path.GetDirectoryName(p.OriginalFilePath);
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
        var p = GetContextRow(sender);
        if (p != null) _viewModel?.RemovePreview(p);
    }

    // -----------------------------------------------------------------
    //  Rename in place — with undo toast
    // -----------------------------------------------------------------

    private async void OnRenameInPlaceClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var pending = _viewModel.PendingRenameCount;
        if (pending == 0)
        {
            ToastService.Info("No files need renaming — every selected row's cleaned name already matches.");
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "Rename files?",
            Content = $"This will rename {pending} file(s) on disk. " +
                      $"Companion subtitles (.srt etc) will be renamed too.\n\n" +
                      $"You'll have ~30 seconds to undo from a toast notification.",
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var opResult = await _viewModel.RenameInPlaceAsync();

        if (opResult.Success && opResult.Errors.Count == 0)
        {
            // Success toast with Undo action button (30s window)
            ToastService.ShowAction(
                message: $"Renamed {pending} file(s).",
                title: "Done",
                actionText: "Undo",
                onAction: () =>
                {
                    if (_viewModel == null) return;
                    var (ok, bad) = _viewModel.UndoLastRename();
                    if (bad == 0) ToastService.Success($"Reverted {ok} rename(s).");
                    else ToastService.Warning($"Reverted {ok}, {bad} failed.");
                });
        }
        else
        {
            var top = string.Join("\n", opResult.Errors.Take(5));
            var more = opResult.Errors.Count > 5 ? $"\n... and {opResult.Errors.Count - 5} more." : "";
            ToastService.Error($"{opResult.Errors.Count} error(s):\n{top}{more}");
        }
    }
}
