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

    /// <summary>
    /// When true, the dialog hits TMDb's /search/tv endpoint instead of /search/movie
    /// — so picking from a TV show card shows TV results, not movies.
    /// </summary>
    public bool TvSearchMode { get; set; }

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

        Title = TvSearchMode ? "Search TMDb (TV shows)" : "Search TMDb (movies)";

        // Default to a comfortably-large window. User can resize / move freely.
        AppWindow.Resize(new SizeInt32(760, 680));

        // Match the app icon on the title bar / taskbar instead of the WinUI default.
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(iconPath))
                AppWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetIcon failed for TMDb search dialog: {ex.Message}");
        }

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

        // Re-apply title now that callers may have set TvSearchMode after the
        // constructor ran.
        Title = TvSearchMode ? "Search TMDb (TV shows)" : "Search TMDb (movies)";

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
            StatusText.Text = "Enter a title, or paste a TMDb ID / link.";
            return;
        }

        // ---- Direct TMDb ID / URL shortcut ----
        // If the box contains a bare number ("27205"), a "tmdb:27205" prefix, or a
        // pasted TMDb URL (https://www.themoviedb.org/movie/27205-inception), fetch
        // that exact entry instead of doing a title search. A life-saver when the
        // title search returns the wrong match or the film is obscure.
        if (TryParseDirectId(rawQuery, out var directId, out var urlIsTv))
        {
            await FetchByIdAsync(directId, urlIsTv);
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
        var what = TvSearchMode ? "TV show" : "movie";
        StatusText.Text = year.HasValue
            ? $"Searching TMDb {what}s for \"{queryForApi}\" ({year})..."
            : $"Searching TMDb {what}s for \"{queryForApi}\"...";
        SearchButton.IsEnabled = false;

        try
        {
            int resultCount = 0;
            if (TvSearchMode)
            {
                // TV: /search/tv returns (id, name, year, posterPath, overview)
                var tvMatches = await _client.SearchTvAsync(queryForApi, year);
                if (tvMatches.Count == 0)
                {
                    StatusText.Text = "No TV-show matches found.";
                    return;
                }
                foreach (var m in tvMatches.Take(20))
                {
                    Results.Add(new TmdbSearchItem
                    {
                        TmdbId = m.TmdbId,
                        Title = m.Name,
                        Year = m.Year,
                        Overview = m.Overview ?? string.Empty,
                        PosterImageUrl = string.IsNullOrEmpty(m.PosterPath)
                            ? null
                            : _client.GetImageUrl(m.PosterPath, "w185")
                    });
                }
                resultCount = tvMatches.Count;
            }
            else
            {
                var matches = await _client.SearchMovieAsync(queryForApi, year);
                if (matches.Count == 0)
                {
                    StatusText.Text = "No movie matches found.";
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
                resultCount = matches.Count;
            }
            StatusText.Text = $"{resultCount} result(s). Pick one and click \"Use Selected\".";
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

    // Matches a TMDb URL and captures whether it's /movie/ or /tv/ plus the id:
    //   https://www.themoviedb.org/movie/27205-inception
    //   themoviedb.org/tv/1396
    private static readonly Regex TmdbUrlPattern = new(
        @"themoviedb\.org/(?<kind>movie|tv)/(?<id>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "tmdb:27205" or "tmdb 27205"
    private static readonly Regex TmdbPrefixPattern = new(
        @"^tmdb[:\s]+(?<id>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Detects a direct TMDb id in the query. Returns the id, and (for URLs only)
    /// whether the URL pointed at a TV show — which lets a pasted /tv/ link force
    /// the TV lookup even if the dialog opened in movie mode.
    /// </summary>
    private static bool TryParseDirectId(string query, out int id, out bool? urlIsTv)
    {
        id = 0;
        urlIsTv = null;

        // TMDb URL — most specific, also tells us movie vs tv.
        var url = TmdbUrlPattern.Match(query);
        if (url.Success && int.TryParse(url.Groups["id"].Value, out id))
        {
            urlIsTv = url.Groups["kind"].Value.Equals("tv", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        // "tmdb:27205"
        var prefixed = TmdbPrefixPattern.Match(query);
        if (prefixed.Success && int.TryParse(prefixed.Groups["id"].Value, out id))
            return true;

        // Bare number — treat as an id. (Year-like 4-digit values are ambiguous,
        // but a user typing just "1994" almost certainly means the TMDb id; a real
        // title search would include words.)
        if (Regex.IsMatch(query, @"^\d{1,7}$") && int.TryParse(query, out id))
            return true;

        return false;
    }

    /// <summary>
    /// Fetches a single TMDb entry by id and shows it as the only result. Honors
    /// the dialog's TV/movie mode, but a pasted /tv/ or /movie/ URL overrides it.
    /// </summary>
    private async Task FetchByIdAsync(int id, bool? urlIsTv)
    {
        var asTv = urlIsTv ?? TvSearchMode;
        Results.Clear();
        UseButton.IsEnabled = false;
        SearchButton.IsEnabled = false;
        StatusText.Text = $"Fetching TMDb {(asTv ? "TV show" : "movie")} #{id}...";

        try
        {
            if (asTv)
            {
                var show = await _client.GetTvDetailsAsync(id);
                if (show == null)
                {
                    StatusText.Text = $"No TV show found with TMDb id {id}.";
                    return;
                }
                Results.Add(new TmdbSearchItem
                {
                    TmdbId = show.TmdbId,
                    Title = show.Name,
                    Year = show.Year,
                    Overview = show.Overview ?? string.Empty,
                    PosterImageUrl = string.IsNullOrEmpty(show.PosterPath)
                        ? null : _client.GetImageUrl(show.PosterPath, "w185")
                });
            }
            else
            {
                var movie = await _client.GetMovieDetailsAsync(id);
                if (movie == null)
                {
                    StatusText.Text = $"No movie found with TMDb id {id}.";
                    return;
                }
                Results.Add(new TmdbSearchItem
                {
                    TmdbId = movie.TmdbId,
                    Title = movie.Title,
                    Year = movie.Year,
                    Overview = movie.Overview ?? string.Empty,
                    PosterImageUrl = string.IsNullOrEmpty(movie.PosterPath)
                        ? null : _client.GetImageUrl(movie.PosterPath, "w185")
                });
            }

            // Auto-select the single result so the user can just hit "Use Selected".
            ResultsList.SelectedIndex = 0;
            StatusText.Text = "Found by TMDb id. Click \"Use Selected\" to confirm.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Lookup failed: {ex.Message}";
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
