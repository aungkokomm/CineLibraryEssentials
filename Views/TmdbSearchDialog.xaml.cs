using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CineLibraryEssentials.Views;

public sealed partial class TmdbSearchDialog : ContentDialog
{
    private readonly TmdbApiClient _client;
    private static readonly Regex YearInQuery = new(
        @"\b(19\d{2}|20[0-3]\d)\b",
        RegexOptions.Compiled);

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

    /// <summary>
    /// Pre-fill the search box from a filename or title string.
    /// Converts "Title (Year).ext" → "Title Year" so the query carries the year too.
    /// </summary>
    public void SetInitialQuery(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            QueryBox.Text = string.Empty;
            return;
        }

        // Drop any extension
        var clean = System.IO.Path.GetFileNameWithoutExtension(source) ?? source;

        // Convert " (YYYY)" → " YYYY" so year is preserved as part of the search text
        clean = Regex.Replace(clean, @"\s*\(\s*(\d{4})\s*\)\s*", " $1 ");

        // Collapse extra whitespace
        clean = Regex.Replace(clean, @"\s+", " ").Trim();

        QueryBox.Text = clean;
    }

    /// <summary>
    /// Pre-fill the search box from an explicit title + year.
    /// </summary>
    public void SetInitialQuery(string title, int year)
    {
        QueryBox.Text = year > 0 ? $"{title} {year}" : title ?? string.Empty;
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
        var rawQuery = QueryBox.Text?.Trim();
        if (string.IsNullOrEmpty(rawQuery))
        {
            StatusText.Text = "Enter a movie title to search.";
            return;
        }

        // Parse out a 4-digit year from the query (if present) and use it as the
        // TMDb primary_release_year filter. This narrows the result set massively
        // when the user knows the year.
        int? year = null;
        var queryForApi = rawQuery;
        var yearMatch = YearInQuery.Match(rawQuery);
        if (yearMatch.Success && int.TryParse(yearMatch.Value, out var yr))
        {
            year = yr;
            // Strip the year from the title portion sent to TMDb
            queryForApi = rawQuery.Remove(yearMatch.Index, yearMatch.Length);
            queryForApi = Regex.Replace(queryForApi, @"\s+", " ").Trim();
        }

        Results.Clear();
        IsPrimaryButtonEnabled = false;
        StatusText.Text = year.HasValue
            ? $"Searching TMDb for \"{queryForApi}\" ({year})..."
            : $"Searching TMDb for \"{queryForApi}\"...";
        SearchButton.IsEnabled = false;

        try
        {
            var matches = await _client.SearchMovieAsync(queryForApi, year);
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
