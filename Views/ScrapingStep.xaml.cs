using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using CineLibraryEssentials.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace CineLibraryEssentials.Views;

public sealed partial class ScrapingStep : UserControl
{
    private ScrapingViewModel? _viewModel;
    private readonly ConfigService _configService = new();

    /// <summary>Raised when the footer "← Back" is clicked.</summary>
    public event EventHandler? BackRequested;

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    public ScrapingStep()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Restore the last view preference
        var pref = _configService.GetPreferredStep3View();
        if (pref == "List")
        {
            ListViewToggle.IsChecked = true;
            ApplyViewPreference("List");
        }
    }

    public void SetViewModel(ScrapingViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.MovieFolders.CollectionChanged += (_, _) => UpdateEmptyState();
        viewModel.EpisodeTitlesAdded += OfferEpisodeRenameUndo;
        viewModel.DuplicatesFound += titles => ToastService.Warning(
            $"In the list twice: {string.Join(", ", titles.Take(5))}{(titles.Count > 5 ? $" and {titles.Count - 5} more" : "")}. "
            + "Look for the orange Duplicate tag: hover it to see where the other one is.",
            "Duplicates", autoDismissMs: 12000);
        viewModel.BatchFinished += OfferScanInCineLibrary;
        NfoEditorWindow.FolderSaved += OnNfoSaved;
        // The editor's Previous / Next follow the cards as they're shown (sort and search)
        NfoEditorWindow.Sequence = () => viewModel.MovieFolders.Where(m => m.HasNfo).Select(m => m.FolderPath).ToList();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScrapingViewModel.IsAllSelected)
                || e.PropertyName == nameof(ScrapingViewModel.IsNoneSelected))
            {
                SyncMasterCheckBoxes();
            }
        };
        UpdateEmptyState();
        SyncMasterCheckBoxes();
    }

    /// <summary>"Add episode titles to file names" renamed files: offer to put the old names back.</summary>
    private void OfferEpisodeRenameUndo(List<EpisodeFileRenamer.Rename> renames)
    {
        ToastService.ShowAction(
            message: $"Added episode titles to {renames.Count} file name{(renames.Count == 1 ? "" : "s")}.",
            title: "Episode titles",
            actionText: "Undo",
            onAction: () =>
            {
                var restored = EpisodeFileRenamer.Undo(renames);
                ToastService.Info($"Put back {restored} original file name{(restored == 1 ? "" : "s")}.");
            },
            severity: InfoBarSeverity.Success);
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

    public void RefreshFromOrganized()
    {
        _viewModel?.LoadFromOrganizedFolders();
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        if (_viewModel == null) return;
        EmptyState.Visibility = _viewModel.MovieFolders.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        var hidden = _viewModel.TotalCount > 0;   // there are cards, the search or Mode hides them
        EmptyTitle.Text = hidden ? "Nothing matches" : "No movies in the list yet";
        EmptyHint.Text = hidden
            ? "No card's title, year or collection has what's in the search box (or the Mode hides it). Press Esc in the box to clear it."
            : "Add a movie folder, drop one onto the window, or come back from Step 2.";
    }

    // -----------------------------------------------------------------
    //  Search (Ctrl+F)
    // -----------------------------------------------------------------

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _searchTimer;

    private void OnFindAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Keyboard);
    }

    // Waits for a pause in the typing, so 400 cards aren't laid out again on every key
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_searchTimer == null)
        {
            _searchTimer = DispatcherQueue.CreateTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(250);
            _searchTimer.IsRepeating = false;
            _searchTimer.Tick += (_, _) =>
            {
                if (_viewModel != null) _viewModel.SearchText = SearchBox.Text;
                UpdateEmptyState();
            };
        }
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape || SearchBox.Text.Length == 0) return;
        e.Handled = true;
        SearchBox.Text = string.Empty;
    }

    // -----------------------------------------------------------------
    //  Keys on a card: Enter opens it, F2 edits, Space ticks, Delete takes it off
    //  the list, and the arrows move between cards
    // -----------------------------------------------------------------

    private void OnCardPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // A click on the poster makes the card the one the keys work on (the
        // buttons and the checkbox take the click themselves). Only while this
        // window is in front: focusing it would pull it back over a details
        // window a double-click just opened.
        if (sender is Control { FocusState: FocusState.Unfocused } card && MainWindowInFront())
            card.Focus(FocusState.Pointer);
    }

    private static bool MainWindowInFront()
        => GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private void OnCardKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not Control { Tag: MovieFolderItem item } card || _viewModel == null) return;
        var onCard = ReferenceEquals(e.OriginalSource, card);
        if (onCard && e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            var index = _viewModel.MovieFolders.IndexOf(item);
            // UniformGridLayout: as many 160-wide columns (6 apart) as fit
            var columns = Math.Max(1, (int)((CardRepeater.ActualWidth + 6) / (160 + 6)));
            var target = e.Key switch
            {
                VirtualKey.Left => index - 1,
                VirtualKey.Right => index + 1,
                VirtualKey.Up => index - columns,
                _ => index + columns,
            };
            e.Handled = true;
            FocusCard(target);
            return;
        }
        e.Handled = HandleCardKey(item, e.Key, onCard);
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { DataContext: MovieFolderItem item } source || _viewModel == null) return;
        var onRow = source is ListViewItem;
        e.Handled = HandleCardKey(item, e.Key, onRow);
    }

    /// <summary>
    /// The keys both views share. Enter and Space only on the card or row itself, so
    /// they still press a focused button. After Delete the focus goes to the card
    /// now in the removed one's place.
    /// </summary>
    private bool HandleCardKey(MovieFolderItem item, VirtualKey key, bool onCard)
    {
        switch (key)
        {
            case VirtualKey.Enter when onCard:
                _ = OpenCardAsync(item);
                return true;
            case VirtualKey.Space when onCard:
                item.IsSelected = !item.IsSelected;
                return true;
            case VirtualKey.F2:
                if (item.HasNfo) OpenNfoEditor(item.FolderPath);
                else ToastService.Info("There's no NFO to edit yet. Scrape it first.");
                return true;
            case VirtualKey.Delete:
                var index = _viewModel!.MovieFolders.IndexOf(item);
                _viewModel.RemoveItemCommand.Execute(item);
                UpdateEmptyState();
                if (_viewModel.MovieFolders.Count > 0)
                {
                    // Whatever now sits where the removed card was
                    var next = _viewModel.MovieFolders[Math.Min(index, _viewModel.MovieFolders.Count - 1)];
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (MovieList.Visibility == Visibility.Visible)
                        {
                            if (MovieList.ContainerFromItem(next) is ListViewItem row) row.Focus(FocusState.Keyboard);
                        }
                        else FocusCard(_viewModel.MovieFolders.IndexOf(next));
                    });
                }
                return true;
            default:
                return false;
        }
    }

    private void FocusCard(int index)
    {
        if (_viewModel == null || index < 0 || index >= _viewModel.MovieFolders.Count) return;
        var element = CardRepeater.GetOrCreateElement(index);
        element.StartBringIntoView();
        (element as Control)?.Focus(FocusState.Keyboard);
    }

    private async void OnRetryFailedClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null) await _viewModel.RetryFailedCommand.ExecuteAsync(null);
    }

    // -----------------------------------------------------------------
    //  Scan in CineLibrary
    // -----------------------------------------------------------------

    /// <summary>After a batch, when CineLibrary is installed: offer to show the results there.</summary>
    private void OfferScanInCineLibrary(List<MovieFolderItem> scraped)
    {
        if (CineLibraryLink.FindExe(_configService) == null) return;   // no CineLibrary here: nothing to offer
        var folders = scraped.Select(i => i.FolderPath).ToList();
        ToastService.ShowAction(
            message: $"Scraped {folders.Count}. See {(folders.Count == 1 ? "it" : "them")} in CineLibrary right away?",
            title: "CineLibrary",
            actionText: "Scan in CineLibrary",
            onAction: () => _ = ScanInCineLibraryAsync(folders),
            autoDismissMs: 20000);
    }

    private async void OnScanInCineLibraryClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null || _viewModel.MovieFolders.Count == 0)
        {
            ToastService.Info("Add some scraped folders first.");
            return;
        }
        var items = _viewModel.MovieFolders.Where(m => m.IsSelected).ToList();
        if (items.Count == 0) items = _viewModel.MovieFolders.ToList();
        await ScanInCineLibraryAsync(items.Select(i => i.FolderPath).ToList());
    }

    private async Task ScanInCineLibraryAsync(IReadOnlyCollection<string> folders)
    {
        var exe = CineLibraryLink.FindExe(_configService);
        if (exe == null)
        {
            // Not installed by its installer (a copied folder, say): ask once where it is
            exe = await PickCineLibraryExeAsync();
            if (exe == null) return;
            _configService.SetCineLibraryExe(exe);
        }
        if (CineLibraryLink.VersionOf(exe) is { } version && version < CineLibraryLink.FirstVersion)
        {
            ToastService.Warning($"This needs CineLibrary {CineLibraryLink.FirstVersion} or newer, and yours is {version}. Update CineLibrary, then try again.");
            return;
        }
        try
        {
            var running = CineLibraryLink.RequestScan(exe, folders);
            var what = $"{folders.Count} folder{(folders.Count == 1 ? "" : "s")}";
            ToastService.Success(running
                ? $"Sent {what} to CineLibrary. It reads them now."
                : $"Starting CineLibrary. It reads the {what} once it's open.");
        }
        catch (Exception ex)
        {
            ToastService.Error($"Couldn't reach CineLibrary: {ex.Message}");
        }
    }

    private async Task<string?> PickCineLibraryExeAsync()
    {
        ToastService.Info("Show Essentials where CineLibrary.exe is. It's asked only once.");
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add(".exe");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return null;
        if (!string.Equals(file.Name, "CineLibrary.exe", StringComparison.OrdinalIgnoreCase))
        {
            ToastService.Warning("That isn't CineLibrary.exe. Pick the CineLibrary.exe in the folder CineLibrary runs from.");
            return null;
        }
        return file.Path;
    }

    private void OnViewToggleClick(object sender, RoutedEventArgs e)
    {
        var view = ListViewToggle.IsChecked == true ? "List" : "Grid";
        ApplyViewPreference(view);
        _configService.SetPreferredStep3View(view);
    }

    private void ApplyViewPreference(string view)
    {
        if (view == "List")
        {
            MovieGrid.Visibility = Visibility.Collapsed;
            MovieList.Visibility = Visibility.Visible;
        }
        else
        {
            MovieGrid.Visibility = Visibility.Visible;
            MovieList.Visibility = Visibility.Collapsed;
        }
    }

    // -----------------------------------------------------------------
    //  Toolbar
    // -----------------------------------------------------------------

    private async void OnAddFolderClick(SplitButton sender, SplitButtonClickEventArgs args)
    {
        if (_viewModel != null)
            await _viewModel.AddFolderCommand.ExecuteAsync(null);
        UpdateEmptyState();
    }

    /// <summary>Add Folder's ▾: the folders added here before, newest first (as in Step 1).</summary>
    private void OnRecentFoldersOpening(object? sender, object e)
    {
        RecentFoldersFlyout.Items.Clear();
        var recents = _configService.GetRecentScrapeFolders();
        if (recents.Count == 0)
        {
            RecentFoldersFlyout.Items.Add(new MenuFlyoutItem { Text = "(no recent folders)", IsEnabled = false });
            return;
        }
        foreach (var path in recents)
        {
            var item = new MenuFlyoutItem { Text = path, Icon = new FontIcon { Glyph = "\uE838" } };
            item.Click += (_, _) =>
            {
                if (!Directory.Exists(path))
                {
                    ToastService.Warning($"Folder no longer exists: {path}");
                    return;
                }
                _viewModel?.AddFromRootFolder(path);
                UpdateEmptyState();
            };
            RecentFoldersFlyout.Items.Add(item);
        }
    }

    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.ClearAll();
        UpdateEmptyState();
    }

    private async void OnScrapeSelectedClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.ScrapeSelectedCommand.ExecuteAsync(null);
    }

    private async void OnScrapeGapsClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.ScrapeGapsOnlyCommand.ExecuteAsync(null);
    }

    private void OnSelectAllToggle(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if (_viewModel.IsAllSelected)
            _viewModel.SelectNoneCommand.Execute(null);
        else
            _viewModel.SelectAllCommand.Execute(null);
        SyncMasterCheckBoxes();
    }

    private async void OnRefreshImdbClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if (_viewModel.MovieFolders.Count == 0)
        {
            ToastService.Info("Add some scraped folders first.");
            return;
        }

        var r = await _viewModel.RefreshImdbRatingsAsync();
        if (r == null) return;   // another batch is still running

        if (r.Updated > 0)
            ToastService.Success(_viewModel.StatusMessage);
        else if (r.NoRating > 0 && r.AlreadyCurrent == 0)
            ToastService.Warning("Couldn't get IMDb ratings right now. Check your internet connection and try again later.");
        else
            ToastService.Info(_viewModel.StatusMessage);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.SaveCommand.Execute(null);
        ToastService.Success("Library is ready! NFO + posters were saved alongside each movie.");
    }

    // -----------------------------------------------------------------
    //  Per-row buttons (work for both grid and list views)
    // -----------------------------------------------------------------

    private async void OnScrapeRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not MovieFolderItem item) return;
        await OpenTmdbDialogForAsync(item);
    }

    private void OnOpenRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MovieFolderItem item)
            _viewModel?.OpenFolderCommand.Execute(item);
    }

    private async void OnDetailsRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MovieFolderItem item)
            await ShowMovieDetailsAsync(item);
    }

    private void OnRemoveRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MovieFolderItem item)
        {
            _viewModel?.RemoveItemCommand.Execute(item);
            UpdateEmptyState();
        }
    }

    // -----------------------------------------------------------------
    //  Double-tap a card: its details, or TMDb search when not scraped yet
    // -----------------------------------------------------------------

    private void OnCardDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (sender is not Border b || b.Tag is not MovieFolderItem item) return;
        e.Handled = true;
        // Once the double-click is over, so nothing from it lands on this window
        // after the details window opened (which would put this one back in front)
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _ = OpenCardAsync(item));
    }

    /// <summary>
    /// Double-click or Enter. Context-aware: folders with an NFO open the details
    /// dialog so the user can verify what was scraped, even when TMDb had no fanart
    /// or cast photos for it. Folders without one open the TMDb search to pick a match.
    /// </summary>
    private async Task OpenCardAsync(MovieFolderItem item)
    {
        if (item.HasNfo)
            await ShowMovieDetailsAsync(item);
        else
            await OpenTmdbDialogForAsync(item);
    }

    private async Task ShowMovieDetailsAsync(MovieFolderItem item)
    {
        var vm = _viewModel;
        var dialog = new MovieDetailsDialog
        {
            LibraryFolders = LibraryFolders(),
            // For the collection check: which of its films are in the list
            OwnedMovieIds = vm == null ? null : () => vm.MovieTmdbIds(),
        };
        if (!dialog.LoadFromFolder(item.FolderPath))
        {
            ToastService.Info("No metadata found for this folder yet - run Scrape first.");
            return;
        }
        await dialog.ShowDialogAsync(App.MainWindow);
    }

    /// <summary>Every folder in the list, for the editor's suggestions while typing.</summary>
    private List<string> LibraryFolders()
        => _viewModel?.MovieFolders.Select(m => m.FolderPath).ToList() ?? new List<string>();

    private void OpenNfoEditor(string folder)
    {
        var (_, error) = NfoEditorWindow.Show(folder, LibraryFolders());
        if (error != null) ToastService.Info(error);
    }

    /// <summary>An NFO was saved or restored in the editor: show the new poster and status.</summary>
    private void OnNfoSaved(string folder)
    {
        var item = _viewModel?.MovieFolders.FirstOrDefault(m =>
            string.Equals(m.FolderPath, folder, StringComparison.OrdinalIgnoreCase));
        if (item == null) return;
        item.PosterPath = null;   // forces the card to load the (maybe replaced) poster again
        _viewModel!.RefreshAssetFlags(item);
        _ = _viewModel.CheckDuplicatesAsync();   // the TMDb id may have changed
    }

    private async void OnBulkEditClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var folders = _viewModel.MovieFolders.Where(m => m.IsSelected).Select(m => m.FolderPath).ToList();
        if (folders.Count == 0)
        {
            ToastService.Info("Tick the cards you want to edit first.");
            return;
        }

        var dialog = new BulkEditDialog(folders, LibraryFolders()) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        if (dialog.Result is not { } r) return;

        foreach (var folder in folders) OnNfoSaved(folder);
        var message = $"Updated {r.Updated} NFO{(r.Updated == 1 ? "" : "s")}"
            + (r.Unchanged > 0 ? $", {r.Unchanged} already matched" : "")
            + (r.NoNfo > 0 ? $", {r.NoNfo} not scraped yet" : "")
            + (r.Failed > 0 ? $", {r.Failed} failed" : "") + ".";
        if (r.Failed > 0) ToastService.Warning(message);
        else ToastService.Success(message);
    }

    // -----------------------------------------------------------------
    //  Context menu (works for both views since we use DataContext)
    // -----------------------------------------------------------------

    private MovieFolderItem? GetContextRow(object sender)
    {
        if (sender is MenuFlyoutItem mfi && mfi.DataContext is MovieFolderItem op) return op;
        return null;
    }

    private async void OnContextScrapeClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextRow(sender);
        if (item != null) await OpenTmdbDialogForAsync(item);
    }

    private async void OnContextViewDetailsClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextRow(sender);
        if (item != null) await ShowMovieDetailsAsync(item);
    }

    private void OnContextEditNfoClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextRow(sender);
        if (item != null) OpenNfoEditor(item.FolderPath);
    }

    private void OnContextOpenClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextRow(sender);
        if (item != null) _viewModel?.OpenFolderCommand.Execute(item);
    }

    private void OnContextRemoveClick(object sender, RoutedEventArgs e)
    {
        var item = GetContextRow(sender);
        if (item != null)
        {
            _viewModel?.RemoveItemCommand.Execute(item);
            UpdateEmptyState();
        }
    }

    // -----------------------------------------------------------------
    //  TMDb dialog
    // -----------------------------------------------------------------

    private async Task OpenTmdbDialogForAsync(MovieFolderItem item)
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
            // Switch the dialog to /search/tv when the card is a TV show so the
            // user sees TV results instead of an empty movie search.
            TvSearchMode = item.IsTvShow,
        };
        dialog.SetInitialQuery(item.MovieTitle, item.Year);

        var selected = await dialog.ShowDialogAsync(App.MainWindow);
        if (selected != null)
        {
            await _viewModel.ScrapeWithTmdbIdAsync(
                item, selected.TmdbId, selected.Title, selected.Year);

            if (item.IsScraped)
                ToastService.Success($"Scraped {item.DisplayName}");
            else if (!string.IsNullOrEmpty(item.ErrorMessage))
                ToastService.Error($"Scrape failed: {item.ErrorMessage}");
        }
    }

    // -----------------------------------------------------------------
    //  Drag-drop folder onto Step 3
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
            int beforeCount = _viewModel.MovieFolders.Count;

            // Recursively find every folder containing video files (any depth)
            foreach (var f in items.OfType<StorageFolder>())
                _viewModel.AddFromRootFolder(f.Path);

            int added = _viewModel.MovieFolders.Count - beforeCount;
            if (added > 0) ToastService.Success($"Added {added} folder(s).");
            UpdateEmptyState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Drop error: {ex.Message}");
        }
    }
}
