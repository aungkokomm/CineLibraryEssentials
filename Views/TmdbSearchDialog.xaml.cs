using System.Collections.ObjectModel;
using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CineLibraryEssentials.Views;

public sealed partial class TmdbSearchDialog : ContentDialog
{
    private readonly TmdbApiClient _client;
    public ObservableCollection<TmdbSearchItem> Results { get; } = new();

    /// <summary>Set after the user clicks "Use Selected".</summary>
    public TmdbSearchItem? SelectedItem { get; private set; }

    public TmdbSearchDialog(string apiKey)
    {
        InitializeComponent();
        _client = new TmdbApiClient(apiKey);
        ResultsList.ItemsSource = Results;
        PrimaryButtonClick += (_, _) => SelectedItem = ResultsList.SelectedItem as TmdbSearchItem;
    }

    public void SetInitialQuery(string query)
    {
        // Strip extension and parens if present
        var clean = System.IO.Path.GetFileNameWithoutExtension(query) ?? string.Empty;
        var parenIdx = clean.IndexOf('(');
        if (parenIdx > 0) clean = clean[..parenIdx].Trim();
        QueryBox.Text = clean;
    }

    private async void OnSearchClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await DoSearchAsync();

    private async void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            await DoSearchAsync();
            e.Handled = true;
        }
    }

    private async Task DoSearchAsync()
    {
        var query = QueryBox.Text?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            StatusText.Text = "Enter a movie title to search.";
            return;
        }

        Results.Clear();
        IsPrimaryButtonEnabled = false;
        StatusText.Text = "Searching TMDb...";
        SearchButton.IsEnabled = false;

        try
        {
            var matches = await _client.SearchMovieAsync(query);
            if (matches.Count == 0)
            {
                StatusText.Text = "No matches found.";
                return;
            }

            foreach (var m in matches.Take(20))
            {
                Results.Add(new TmdbSearchItem
                {
                    TmdbId = m.TmdbId,
                    Title = m.Title,
                    Year = m.Year,
                    Overview = m.Overview ?? string.Empty,
                    PosterImageUrl = string.IsNullOrEmpty(m.PosterPath)
                        ? null
                        : _client.GetImageUrl(m.PosterPath, "w185")
                });
            }
            StatusText.Text = $"{matches.Count} result(s). Pick one and click \"Use Selected\".";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = ResultsList.SelectedItem != null;
    }
}

public class TmdbSearchItem
{
    public int TmdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Overview { get; set; } = string.Empty;
    public string? PosterImageUrl { get; set; }

    public string TitleWithYear => Year > 0 ? $"{Title} ({Year})" : Title;
}
