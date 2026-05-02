using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using CineLibraryEssentials.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CineLibraryEssentials.Views;

public sealed partial class ScrapingStep : UserControl
{
    private ScrapingViewModel? _viewModel;
    private readonly ConfigService _configService = new();

    public ScrapingStep()
    {
        InitializeComponent();
    }

    public void SetViewModel(ScrapingViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        MovieList.ItemsSource = viewModel.MovieFolders;
    }

    public void RefreshFromOrganized()
    {
        _viewModel?.LoadFromOrganizedFolders();
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.AddFolderCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Per-row Scrape: opens the TMDb search dialog so the user picks the correct
    /// match, then scrapes that specific TMDb id (downloads metadata, poster, fanart,
    /// actor photos, NFO).
    /// </summary>
    private async void OnScrapeRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not MovieFolderItem item) return;
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
        dialog.SetInitialQuery(item.MovieTitle);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.SelectedItem != null)
        {
            await _viewModel.ScrapeWithTmdbIdAsync(
                item,
                dialog.SelectedItem.TmdbId,
                dialog.SelectedItem.Title,
                dialog.SelectedItem.Year);
        }
    }

    private void OnOpenRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MovieFolderItem item)
        {
            _viewModel?.OpenFolderCommand.Execute(item);
        }
    }

    private void OnRemoveRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MovieFolderItem item)
        {
            _viewModel?.RemoveItemCommand.Execute(item);
        }
    }

    /// <summary>
    /// "Scrape Selected" auto-scrape — uses the first TMDb match. Useful when the
    /// auto-detected titles are already correct and you want to batch-process.
    /// </summary>
    private async void OnScrapeSelectedClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.ScrapeSelectedCommand.ExecuteAsync(null);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.SaveCommand.Execute(null);
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
