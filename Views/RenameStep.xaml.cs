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
    private bool _tipShown;

    /// <summary>Raised when the footer "← Back" is clicked. The wizard handles it.</summary>
    public event EventHandler? BackRequested;

    public RenameStep()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            RebuildRecentFoldersFlyout();
            ShowTipToastOnce();
        };
    }

    /// <summary>
    /// Shows the Step 1 tip as a floating toast (once per session, unless the user
    /// dismissed it permanently). Replaces the old fixed InfoBar row so it costs
    /// zero layout space.
    /// </summary>
    private void ShowTipToastOnce()
    {
        if (_tipShown) return;
        _tipShown = true;
        if (_configService.IsWarningDismissed(WarningId)) return;

        var bar = ToastService.ShowAction(
            message: "Already-clean files are hidden — turn off \"Modified only\" to see them. " +
                     "Review Low / Medium rows carefully; you get a 30-second Undo after each rename.",
            title: "Tip",
            actionText: "Don't show again",
            onAction: () => _configService.DismissWarning(WarningId),
            autoDismissMs: 12000);
        // Nothing else to wire — the action button records the dismissal.
        _ = bar;
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
            else if (e.PropertyName == nameof(RenameViewModel.IsAllSelected)
                  || e.PropertyName == nameof(RenameViewModel.IsNoneSelected))
            {
                SyncMasterCheckBoxes();
            }
        };
        UpdateSortIndicators();
        SyncMasterCheckBoxes();

        // Re-open the last-used source folder on startup so the user doesn't have
        // to re-pick it every launch. Fire-and-forget; no-op if there's no history.
        _ = viewModel.TryLoadLastFolderAsync();
    }

    private void SyncMasterCheckBoxes()
    {
        if (_viewModel == null) return;
        // Tri-state: all → checked, none → unchecked, partial → indeterminate (dash).
        SelectAllCheckBox.IsChecked = _viewModel.IsAllSelected
            ? true
            : _viewModel.IsNoneSelected
                ? false
                : (bool?)null;
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    // -----------------------------------------------------------------
    //  Draggable column divider (Original ↔ Cleaned)
    // -----------------------------------------------------------------

    private void OnColumnDividerDrag(object sender, Microsoft.UI.Xaml.Input.ManipulationDeltaRoutedEventArgs e)
    {
        if (_viewModel == null) return;

        // Convert the pixel drag into a change in the star ratio between the two
        // flexible columns. Fixed columns total 36 + 8(divider) + 80 + 32 + 32 = 188.
        var flexible = HeaderGrid.ActualWidth - 188;
        if (flexible <= 0) return;

        var cols = _viewModel.Columns;
        var origStar = cols.OriginalWidth.Value;   // current star weights (sum ~2)
        var cleanStar = cols.CleanedWidth.Value;
        var total = origStar + cleanStar;
        if (total <= 0) return;

        // Current original pixel width, shifted by the drag.
        var origPx = flexible * (origStar / total) + e.Delta.Translation.X;
        var ratio = Math.Clamp(origPx / flexible, 0.15, 0.85);

        cols.OriginalWidth = new GridLength(ratio, GridUnitType.Star);
        cols.CleanedWidth = new GridLength(1 - ratio, GridUnitType.Star);
    }

    private void OnDividerPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
            Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
    }

    private void OnDividerPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        this.ProtectedCursor = null;
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

    private void OnSelectAllToggle(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        // If everything is already selected, clicking clears all; otherwise
        // (none or partial) it selects all. SyncMasterCheckBoxes then overrides
        // whatever the checkbox's default toggle did with the true state.
        if (_viewModel.IsAllSelected)
            _viewModel.SelectNoneCommand.Execute(null);
        else
            _viewModel.SelectAllCommand.Execute(null);
        SyncMasterCheckBoxes();
    }
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
        // Ctrl+A toggles, same as clicking the master checkbox: select all, or
        // deselect all if everything is already selected.
        if (_viewModel == null) return;
        if (_viewModel.IsAllSelected)
            _viewModel.SelectNoneCommand.Execute(null);
        else
            _viewModel.SelectAllCommand.Execute(null);
        SyncMasterCheckBoxes();
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

        var dialog = new TmdbSearchDialog(apiKey, _configService.GetScrapeLanguage())
        {
            // For TV episode rows, search the show name on /search/tv instead
            // of /search/movie so the dialog returns the right kind of result.
            TvSearchMode = preview.IsTvEpisode,
        };
        dialog.SetInitialQuery(preview.IsTvEpisode && !string.IsNullOrEmpty(preview.ShowName)
            ? preview.ShowName
            : preview.CleanedName);

        var selected = await dialog.ShowDialogAsync(App.MainWindow);
        if (selected != null)
        {
            _viewModel.ApplyTmdbMatch(preview, selected.Title, selected.Year);
            ToastService.Success($"Applied TMDb match: {selected.TitleWithYear}");
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
        var cleanOnly = pending == 0 && _viewModel.CleanEmbeddedMetadata;
        var selectedCount = _viewModel.SelectedCount;

        // Nothing to do AT ALL: no renames pending and Clean Metadata isn't ticked.
        if (pending == 0 && !_viewModel.CleanEmbeddedMetadata)
        {
            ToastService.Info("No files need renaming — every selected row's cleaned name already matches. Tick 'Clean metadata' if you only want to scrub embedded tags.");
            return;
        }

        // Metadata cleaning rewrites each file's container in place — it's the
        // slow part of the operation, so warn the user up front when it's on.
        var cleaningMetadata = _viewModel.CleanEmbeddedMetadata;
        const string patienceNote =
            "\n\n⏳ Cleaning embedded metadata rewrites each file and can take a while " +
            "for large videos — please be patient and don't close the app until it finishes.";

        // Build a dialog message that reflects what's actually about to happen.
        string title, content, primary;
        if (cleanOnly)
        {
            title = "Clean embedded metadata?";
            content = $"All {selectedCount} selected file(s) are already named correctly. " +
                      $"This will scrub their embedded title, comment, tags, track names and " +
                      $"attachments (e.g. logo images) using the bundled mkvpropedit.\n\n" +
                      $"The video/audio data itself is not touched." + patienceNote;
            primary = "Clean Metadata";
        }
        else
        {
            title = "Rename files?";
            content = $"This will rename {pending} file(s) on disk. " +
                      $"Companion subtitles (.srt etc) will be renamed too.\n\n" +
                      $"You'll have ~30 seconds to undo from a toast notification.";
            if (cleaningMetadata) content += patienceNote;
            primary = "Rename";
        }

        var confirm = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        // The determinate progress bar in the footer (bound to IsProcessing /
        // ProgressValue) now communicates the slow metadata pass, so no toast
        // is needed here.
        var opResult = await _viewModel.RenameInPlaceAsync();

        if (opResult.Success && opResult.Errors.Count == 0)
        {
            if (cleanOnly)
            {
                ToastService.Success($"Cleaned metadata on {selectedCount} file(s).");
            }
            else
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
        }
        else
        {
            var top = string.Join("\n", opResult.Errors.Take(5));
            var more = opResult.Errors.Count > 5 ? $"\n... and {opResult.Errors.Count - 5} more." : "";
            ToastService.Error($"{opResult.Errors.Count} error(s):\n{top}{more}");
        }
    }
}
