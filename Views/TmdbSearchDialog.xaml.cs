using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;

namespace CineLibraryEssentials.Views;

/// <summary>
/// Resizable, moveable TMDb picker. Implemented as a standalone Window (rather
/// than a ContentDialog) so the user can drag, resize, and even keep it open
/// off to the side of the main wizard.
///
/// Modal-ish behavior is provided by <see cref="ShowDialogAsync"/>: the caller
/// awaits a Task that completes when the user picks a result, cancels, or
/// closes the window.
/// </summary>
public sealed partial class TmdbSearchDialog : Window
{
    private readonly TmdbApiClient _client;
    private TaskCompletionSource<TmdbSearchItem?>? _completionSource;

    private static readonly Regex YearInQuery = new(
        @"\b(19\d{2}|20[0-3]\d)\b",
        RegexOptions.Compiled);

    public ObservableCollection<TmdbSearchItem> Results { get; } = new();

    /// <summary>Mirror of the picked row — set when the user clicks "Use Selected".</summary>
    public TmdbSearchItem? SelectedItem { get; private set; }

    public TmdbSearchDialog(string apiKey, string language = "en")
    {
        InitializeComponent();
        _client = new TmdbApiClient(apiKey, language);
        ResultsList.ItemsSource = Results;

        Title = "Search TMDb";

        // Default to a comfortably-large window. User can resize / move freely.
        AppWindow.Resize(new SizeInt32(760, 680));

        // If user closes via the title-bar X, treat as cancel.
        Closed += (_, _) => _completionSource?.TrySetResult(null);
    }

    /// <summary>
    /// Shows the dialog and waits asynchronously for the user to pick a result
    /// or close. Returns the selected item or null on cancel.
    /// Optionally centers on an owner window.
    /// </summary>
    public Task<TmdbSearchItem?> ShowDialogAsync(Window? owner = null)
    {
        _completionSource = new TaskCompletionSource<TmdbSearchItem?>();

        // Center on the owner window when one is provided so the picker pops up
        // in a sensible place instead of the top-left of the primary monitor.
        if (owner is not null)
        {
            try
            {
                var ownerPos = owner.AppWindow.Position;
                var ownerSize = owner.AppWindow.Size;
                var mySize = AppWindow.Size;
                AppWindow.Move(new PointInt32(
                    ownerPos.X + (ownerSize.Width - mySize.Width) / 2,
                    ownerPos.Y + (ownerSize.Height - mySize.Height) / 2));
            }
            catch { /* if positioning fails, just open at default */ }
        }

        Activate();
        QueryBox.Focus(FocusState.Programmatic);
        return _completionSource.Task;
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

        var clean = System.IO.Path.GetFileNameWithoutExtension(source) ?? source;
        clean = Regex.Replace(clean, @"\s*\(\s*(\d{4})\s*\)\s*", " $1 ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        QueryBox.Text = clean;
    }

    /// <summary>Pre-fill the search box from an explicit title + year.</summary>
    public void SetInitialQuery(string title, int year)
    {
        QueryBox.Text = year > 0 ? $"{title} {year}" : title ?? string.Empty;
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e)
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

        // Parse a 4-digit year out of the query — TMDb's primary_release_year
        // filter narrows the result set massively when the year is known.
        int? year = null;
        var queryForApi = rawQuery;
        var yearMatch = YearInQuery.Match(rawQuery);
        if (yearMatch.Success && int.TryParse(yearMatch.Value, out var yr))
        {
            year = yr;
            queryForApi = rawQuery.Remove(yearMatch.Index, yearMatch.Length);
            queryForApi = Regex.Replace(queryForApi, @"\s+", " ").Trim();
        }

        Results.Clear();
        UseButton.IsEnabled = false;
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
        UseButton.IsEnabled = ResultsList.SelectedItem != null;
    }

    private void OnUseClick(object sender, RoutedEventArgs e)
    {
        SelectedItem = ResultsList.SelectedItem as TmdbSearchItem;
        _completionSource?.TrySetResult(SelectedItem);
        this.Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _completionSource?.TrySetResult(null);
        this.Close();
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
