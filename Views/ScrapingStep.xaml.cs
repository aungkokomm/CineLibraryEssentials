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
        UpdateEmptyState();
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
            GridScroll.Visibility = Visibility.Collapsed;
            MovieList.Visibility = Visibility.Visible;
        }
        else
        {
            GridScroll.Visibility = Visibility.Visible;
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
    //  Card hover effect + double-tap to scrape
    // -----------------------------------------------------------------

    private void OnCardPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Grid g) g.Opacity = 1;
    }

    private void OnCardPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Grid g) g.Opacity = 0;
    }

    private async void OnCardDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (sender is Border b && b.Tag is MovieFolderItem item)
            await OpenTmdbDialogForAsync(item);
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

        var dialog = new TmdbSearchDialog(apiKey) { XamlRoot = this.XamlRoot };
        dialog.SetInitialQuery(item.MovieTitle, item.Year);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.SelectedItem != null)
        {
            await _viewModel.ScrapeWithTmdbIdAsync(
                item, dialog.SelectedItem.TmdbId,
                dialog.SelectedItem.Title, dialog.SelectedItem.Year);

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
            foreach (var f in items.OfType<StorageFolder>())
            {
                // If user drops a parent folder containing many movie folders, add each.
                // Otherwise add the dropped folder directly.
                var subFolders = Directory.GetDirectories(f.Path)
                    .Where(d => Directory.EnumerateFiles(d).Any(Utilities.FileFormatValidator.IsVideoFile))
                    .ToList();
                if (subFolders.Count > 0)
                {
                    foreach (var sub in subFolders)
                        _viewModel.AddSingleFolder(sub);
                }
                else
                {
                    _viewModel.AddSingleFolder(f.Path);
                }
            }
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
