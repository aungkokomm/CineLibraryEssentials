using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using CineLibraryEssentials.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

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
        NfoEditorWindow.FolderSaved += OnNfoSaved;
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

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.AddFolderCommand.ExecuteAsync(null);
        UpdateEmptyState();
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

    private void OnRemoveRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MovieFolderItem item)
        {
            _viewModel?.RemoveItemCommand.Execute(item);
            UpdateEmptyState();
        }
    }

    // -----------------------------------------------------------------
    //  Double-tap a card to open TMDb search
    // -----------------------------------------------------------------

    private async void OnCardDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (sender is not Border b || b.Tag is not MovieFolderItem item) return;

        // Context-aware: already-scraped folders open the details dialog so the
        // user can verify what was scraped. Unscraped/failed folders open the
        // TMDb search so they can pick a match.
        if (item.IsScraped)
            await ShowMovieDetailsAsync(item);
        else
            await OpenTmdbDialogForAsync(item);
    }

    private async Task ShowMovieDetailsAsync(MovieFolderItem item)
    {
        var dialog = new MovieDetailsDialog { LibraryFolders = LibraryFolders() };
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
