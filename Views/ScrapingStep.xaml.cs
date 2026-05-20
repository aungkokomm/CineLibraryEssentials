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

    private void SyncMasterCheckBoxes()
    {
        if (_viewModel == null) return;
        AllCheckBox.IsChecked = _viewModel.IsAllSelected;
        NoneCheckBox.IsChecked = _viewModel.IsNoneSelected;
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

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.SelectAllCommand.Execute(null);
        SyncMasterCheckBoxes();
    }

    private void OnSelectNoneClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.SelectNoneCommand.Execute(null);
        SyncMasterCheckBoxes();
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
        var dialog = new MovieDetailsDialog();
        if (!dialog.LoadFromFolder(item.FolderPath))
        {
            ToastService.Info("No metadata found for this folder yet — run Scrape first.");
            return;
        }
        await dialog.ShowDialogAsync(App.MainWindow);
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
