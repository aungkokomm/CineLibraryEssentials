using System.Collections.ObjectModel;
using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace CineLibraryEssentials.ViewModels;

public partial class ScrapingViewModel : ObservableObject
{
    private readonly ConfigService _configService = new();
    private readonly WizardViewModel _parentViewModel;
    private readonly DispatcherQueue _dispatcherQueue;
    private ScraperService? _scraperService;

    [ObservableProperty]
    private ObservableCollection<MovieFolderItem> movieFolders = new();

    [ObservableProperty]
    private bool isWorking = false;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    /// <summary>
    /// "Auto", "Movies", or "TvShows" — wizard mode. Mirrors Step 1's setting via
    /// the shared config so changing it in either place propagates. In Step 3 it
    /// acts as a display filter: Movies hides TV shows, TvShows hides movies.
    /// </summary>
    [ObservableProperty]
    private string wizardMode = "Auto";

    partial void OnWizardModeChanged(string value)
    {
        _configService.SetWizardMode(value);
        ApplyModeFilter();
    }

    /// <summary>
    /// Master list of everything Step 3 has discovered. The bound MovieFolders
    /// collection is a filtered view of this — what's actually visible depends
    /// on WizardMode (Movies hides TV shows, TvShows hides movies, Auto shows all).
    /// All adds/removes go through the AddItem / RemoveItem / ClearItems helpers
    /// below so both stay in sync.
    /// </summary>
    private readonly List<MovieFolderItem> _allFolders = new();

    private bool ShouldShow(MovieFolderItem item) =>
        WizardMode switch
        {
            "Movies"  => !item.IsTvShow,
            "TvShows" => item.IsTvShow,
            _         => true,
        };

    private void AddItem(MovieFolderItem item)
    {
        _allFolders.Add(item);
        if (ShouldShow(item)) MovieFolders.Add(item);
    }

    private void RemoveItemInternal(MovieFolderItem item)
    {
        _allFolders.Remove(item);
        MovieFolders.Remove(item);
    }

    private void ClearItems()
    {
        _allFolders.Clear();
        MovieFolders.Clear();
    }

    private void ApplyModeFilter()
    {
        MovieFolders.Clear();
        foreach (var item in _allFolders)
            if (ShouldShow(item)) MovieFolders.Add(item);
    }

    public ScrapingViewModel(WizardViewModel parentViewModel)
    {
        _parentViewModel = parentViewModel;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Watch for items added/removed so we can subscribe / unsubscribe to their
        // PropertyChanged — needed to keep the All/None toggle states accurate.
        MovieFolders.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (MovieFolderItem item in e.NewItems)
                    item.PropertyChanged += OnMovieItemPropertyChanged;
            if (e.OldItems != null)
                foreach (MovieFolderItem item in e.OldItems)
                    item.PropertyChanged -= OnMovieItemPropertyChanged;
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(IsNoneSelected));
        };

        // Initialize WizardMode from the shared config (Step 1's dropdown writes
        // here too) so the filter applies as soon as Step 3 loads.
        wizardMode = _configService.GetWizardMode();
    }

    private void OnMovieItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MovieFolderItem.IsSelected))
        {
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(IsNoneSelected));
        }
    }

    /// <summary>
    /// Auto-populate movie folders from Step 2's organized output.
    /// Called when entering Step 3.
    /// </summary>
    public void LoadFromOrganizedFolders()
    {
        var outputFolder = _parentViewModel.SelectedOutputFolder;
        if (string.IsNullOrEmpty(outputFolder) || !Directory.Exists(outputFolder))
            return;

        // Get unique destination folders from Step 2's operations
        var folderPaths = _parentViewModel.AllFileOperations
            .Select(op => op.DestinationFolder)
            .Distinct()
            .Where(Directory.Exists)
            .ToList();

        foreach (var folderPath in folderPaths)
        {
            // Skip if already added (check the master list so the filter doesn't
            // accidentally let the same folder be added twice while hidden).
            if (_allFolders.Any(m => string.Equals(m.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)))
                continue;

            AddItem(CreateMovieItem(folderPath));
        }
    }

    [RelayCommand]
    public async Task AddFolderAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            AddFromRootFolder(folder.Path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Recursively finds every folder under <paramref name="rootPath"/> that contains
    /// at least one video file (at any depth) and adds each one as a movie item.
    /// Handles arbitrary nesting like Genre/Year/Movie/file.mkv.
    /// </summary>
    public void AddFromRootFolder(string rootPath)
    {
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;

        int added = 0, hidden = 0;
        foreach (var movieFolder in EnumerateMovieFolders(rootPath))
        {
            if (_allFolders.Any(m => string.Equals(m.FolderPath, movieFolder, StringComparison.OrdinalIgnoreCase)))
                continue;

            var item = CreateMovieItem(movieFolder);
            _allFolders.Add(item);
            if (ShouldShow(item))
            {
                MovieFolders.Add(item);
                added++;
            }
            else
            {
                hidden++;
            }
        }

        // Tell the user when items were added but hidden by the current Mode filter,
        // so "Add Folder" never looks silently broken.
        if (added == 0 && hidden == 0)
        {
            StatusMessage = "No movie or TV-show folders found under that path.";
        }
        else if (hidden > 0)
        {
            var label = WizardMode == "TvShows" ? "movies" : "TV shows";
            StatusMessage = added > 0
                ? $"Added {added} item(s). {hidden} {label} hidden by the Mode filter — switch to Auto to see them."
                : $"Added {hidden} {label}, but Mode filter is hiding them. Switch to Auto to see them.";
        }
        else
        {
            StatusMessage = $"Added {added} folder(s).";
        }
    }

    /// <summary>
    /// Walks the directory tree from <paramref name="root"/> and yields one folder
    /// per movie OR per TV show. A folder counts as a TV show root when at least
    /// one of its direct children is a "Season XX" folder containing videos —
    /// in that case the SHOW folder is yielded (not the individual season folders)
    /// so Step 3 shows one card per show, not one per season.
    /// </summary>
    public static IEnumerable<string> EnumerateMovieFolders(string root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) yield break;

        // Folders we've already classified as part of a TV show (season subdirs +
        // the show root itself). We use this so the recursive walk below doesn't
        // also yield each Season folder as a separate "movie".
        var tvDescendants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check root + every direct subdir for being a TV show root first.
        TryClassifyTvShow(root, tvDescendants);

        string[] subDirs;
        try { subDirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories); }
        catch { yield break; }

        foreach (var sub in subDirs)
            TryClassifyTvShow(sub, tvDescendants);

        // Now walk and yield: TV show roots when we hit them, movie folders otherwise.
        // We yield show roots in priority — anything BELOW a known show root is
        // skipped (we don't want one card per season).

        // First, yield identified TV show roots
        // Reuse the same set — anything classified as "show root" is the actual yield target.
        // To get them, we need to know which entries in tvDescendants were ROOTS vs descendants.
        // Easier path: re-scan and track roots separately.

        var shownRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Multi-season show roots first (so flat season folders nested under
        //    them aren't double-yielded).
        if (IsTvShowRoot(root))
        {
            yield return root;
            shownRoots.Add(root);
        }
        foreach (var sub in subDirs)
        {
            if (IsTvShowRoot(sub) && !IsUnderAnyShown(sub, shownRoots))
            {
                yield return sub;
                shownRoots.Add(sub);
            }
        }

        // 2) Flat single-season TV folders (episodes directly inside).
        if (!shownRoots.Contains(root) && !IsUnderAnyShown(root, shownRoots)
            && IsFlatTvSeasonFolder(root))
        {
            yield return root;
            shownRoots.Add(root);
        }
        foreach (var sub in subDirs)
        {
            if (shownRoots.Contains(sub)) continue;
            if (IsUnderAnyShown(sub, shownRoots)) continue;
            if (IsFlatTvSeasonFolder(sub))
            {
                yield return sub;
                shownRoots.Add(sub);
            }
        }

        // 3) Remaining folders with videos = movie folders.
        if (!shownRoots.Contains(root) && !IsUnderAnyShown(root, shownRoots) && HasVideoFile(root))
            yield return root;

        foreach (var sub in subDirs)
        {
            if (shownRoots.Contains(sub)) continue;
            if (IsUnderAnyShown(sub, shownRoots)) continue;
            if (HasVideoFile(sub)) yield return sub;
        }
    }

    /// <summary>True if <paramref name="folder"/> contains at least one
    /// "Season XX" subfolder that itself contains a video file. (Multi-season
    /// show root — the classic Kodi/Plex layout.)</summary>
    public static bool IsTvShowRoot(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(folder))
            {
                var name = Path.GetFileName(sub);
                if (LooksLikeSeasonFolder(name) && HasVideoFile(sub))
                    return true;
            }
        }
        catch { /* permissions */ }
        return false;
    }

    /// <summary>
    /// True if <paramref name="folder"/> looks like a single-season folder —
    /// no Season XX subdir, but its direct video files have TV episode patterns
    /// (S01E01 etc.). Common in pre-organized libraries like
    /// "Breaking Bad Season 1 Complete/" or just "Show S01/".
    /// </summary>
    public static bool IsFlatTvSeasonFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
        try
        {
            int videoCount = 0, tvCount = 0;
            foreach (var f in Directory.EnumerateFiles(folder))
            {
                if (!Utilities.FileFormatValidator.IsVideoFile(f)) continue;
                videoCount++;
                if (Utilities.RegexPatterns.IsTvEpisode(Path.GetFileName(f)))
                    tvCount++;
            }
            // At least 2 videos and majority match the TV pattern. The "2"
            // minimum stops a single SxxExx-named file in a movie folder
            // (rare) from flipping the whole folder to TV.
            return videoCount >= 2 && tvCount * 2 >= videoCount;
        }
        catch { return false; }
    }

    /// <summary>Either a show root with Season subdirs, or a flat season folder.</summary>
    public static bool IsTvFolder(string folder)
        => IsTvShowRoot(folder) || IsFlatTvSeasonFolder(folder);

    private static readonly System.Text.RegularExpressions.Regex SeasonFolderPattern =
        new(@"^(?:Season|Specials?)[\s_\.]?(\d{1,3})?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool LooksLikeSeasonFolder(string folderName)
        => !string.IsNullOrEmpty(folderName) && SeasonFolderPattern.IsMatch(folderName);

    private static bool IsUnderAnyShown(string path, HashSet<string> shownRoots)
    {
        foreach (var root in shownRoots)
        {
            if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void TryClassifyTvShow(string folder, HashSet<string> tvDescendants)
    {
        if (!IsTvShowRoot(folder)) return;
        tvDescendants.Add(folder);
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(folder))
                tvDescendants.Add(sub);
        }
        catch { }
    }

    [RelayCommand]
    public async Task ScrapeOneAsync(MovieFolderItem item)
    {
        if (item == null || item.IsScraping)
            return;

        EnsureScraperService();

        item.IsScraping = true;
        item.Status = "Scraping...";
        item.ErrorMessage = string.Empty;

        var progressInner = new Progress<string>(s =>
            _dispatcherQueue.TryEnqueue(() => item.Status = s));

        // TV show path uses the dedicated tv scraper (writes tvshow.nfo + per-
        // episode .nfo + episode thumbnails). Movies go through the movie path.
        if (item.IsTvShow)
        {
            var (tvOk, tvMsg) = await _scraperService!.ScrapeTvShowAsync(item.FolderPath, progressInner);
            item.IsScraping = false;
            if (tvOk)
            {
                RefreshAssetFlags(item);
                item.Status = tvMsg; // "Scraped 'Show' — 23 episode(s)"
            }
            else
            {
                item.Status = "Failed";
                item.ErrorMessage = tvMsg;
            }
            return;
        }

        var progress = new Progress<string>(s =>
        {
            _dispatcherQueue.TryEnqueue(() => item.Status = s);
        });

        var (success, message) = await _scraperService!.ScrapeAndDownloadMetadataAsync(
            item.FolderPath, item.MovieTitle, item.Year, progress);

        item.IsScraping = false;
        if (success)
        {
            // Re-detect asset flags from disk so HasNfo/HasPoster/HasFanart/
            // HasActorPhotos + Status update correctly (drives the verify-library
            // missing-asset display). IsScraped/IsComplete are derived from these.
            RefreshAssetFlags(item);
        }
        else
        {
            item.Status = "Failed";
            item.ErrorMessage = message;
        }
    }

    /// <summary>
    /// Re-checks an item's folder on disk and updates the asset-presence flags
    /// and Status. Called after every scrape so the UI shows the new state, and
    /// also exposed publicly so the UI can manually re-verify after the user
    /// makes external file changes.
    /// </summary>
    public void RefreshAssetFlags(MovieFolderItem item)
    {
        if (item == null || !Directory.Exists(item.FolderPath)) return;
        try
        {
            string? poster;
            string? fanart;
            bool nfo;
            if (item.IsTvShow)
            {
                // Show-root convention: poster.jpg, fanart.jpg, tvshow.nfo
                var p = Path.Combine(item.FolderPath, "poster.jpg");
                var f = Path.Combine(item.FolderPath, "fanart.jpg");
                poster = File.Exists(p) ? p : null;
                fanart = File.Exists(f) ? f : null;
                nfo    = File.Exists(Path.Combine(item.FolderPath, "tvshow.nfo"));
            }
            else
            {
                // Movie convention: <basename>-poster.jpg / -fanart.jpg / *.nfo
                poster = Directory.GetFiles(item.FolderPath, "*-poster.jpg").FirstOrDefault();
                fanart = Directory.GetFiles(item.FolderPath, "*-fanart.jpg").FirstOrDefault();
                nfo    = Directory.GetFiles(item.FolderPath, "*.nfo").Any();
            }

            var actorsDir = Path.Combine(item.FolderPath, ".actors");
            var actors = Directory.Exists(actorsDir)
                         && Directory.EnumerateFiles(actorsDir, "*.jpg").Any();

            item.HasNfo = nfo;
            item.HasPoster = poster != null;
            item.HasFanart = fanart != null;
            item.HasActorPhotos = actors;
            item.PosterPath = poster;
            item.IsScraped = item.IsComplete;

            // Keep the TV "N seasons" prefix when refreshing a TV show.
            var verify = BuildVerifyStatus(nfo, poster != null, fanart != null, actors);
            if (item.IsTvShow)
            {
                var seasonCount = CountSeasons(item.FolderPath);
                var label = seasonCount > 0
                    ? $"TV · {seasonCount} season{(seasonCount == 1 ? "" : "s")}"
                    : "TV";
                item.Status = $"{label} · {verify}";
            }
            else
            {
                item.Status = verify;
            }
        }
        catch { /* permissions / disconnected drive — leave existing state */ }
    }

    /// <summary>
    /// Scrapes a single movie OR TV show using a SPECIFIC TMDb id picked by the user
    /// from the search dialog. Dispatches to the right scraper based on item.IsTvShow.
    /// </summary>
    public async Task ScrapeWithTmdbIdAsync(MovieFolderItem item, int tmdbId, string canonicalTitle, int canonicalYear)
    {
        if (item == null || item.IsScraping)
            return;

        EnsureScraperService();

        // Update item to show the user's chosen canonical title/year
        item.MovieTitle = canonicalTitle;
        item.Year = canonicalYear;

        item.IsScraping = true;
        item.Status = "Scraping...";
        item.ErrorMessage = string.Empty;

        var progress = new Progress<string>(s =>
        {
            _dispatcherQueue.TryEnqueue(() => item.Status = s);
        });

        bool success;
        string message;
        if (item.IsTvShow)
        {
            // TV path: use the TV id directly — no auto-search. ScrapeTvShowAsync's
            // overload accepts a known id so we skip the "pick best match" step.
            (success, message) = await _scraperService!.ScrapeTvShowByIdAsync(
                item.FolderPath, tmdbId, progress);
        }
        else
        {
            (success, message) = await _scraperService!.ScrapeByTmdbIdAsync(
                item.FolderPath, tmdbId, progress);
        }

        item.IsScraping = false;
        if (success)
        {
            RefreshAssetFlags(item);
        }
        else
        {
            item.Status = "Failed";
            item.ErrorMessage = message;
        }
    }

    [RelayCommand]
    public async Task ScrapeSelectedAsync()
    {
        IsWorking = true;
        StatusMessage = "Scraping selected movies...";

        // TV shows are scraped via the dedicated TV path inside ScrapeOneAsync,
        // so we include them here just like movies.
        var selected = MovieFolders
            .Where(m => m.IsSelected && !m.IsScraped)
            .ToList();
        for (int i = 0; i < selected.Count; i++)
        {
            StatusMessage = $"Scraping {i + 1} of {selected.Count}: {selected[i].DisplayName}";
            await ScrapeOneAsync(selected[i]);
            await Task.Delay(200);
        }

        IsWorking = false;
        StatusMessage = $"Done. {selected.Count(s => s.IsScraped)} of {selected.Count} succeeded.";
    }

    /// <summary>
    /// "Verify library" workflow: scrape every folder in the list that has
    /// ANY missing asset (NFO / poster / fanart / .actors/), ignoring the
    /// IsSelected checkbox. Fully-complete folders are skipped, so re-running
    /// after a partial scrape only fills the gaps.
    /// </summary>
    [RelayCommand]
    public async Task ScrapeGapsOnlyAsync()
    {
        var gaps = MovieFolders.Where(m => !m.IsComplete).ToList();
        if (gaps.Count == 0)
        {
            StatusMessage = "All folders already have a full set of assets.";
            return;
        }

        IsWorking = true;
        StatusMessage = $"Filling gaps in {gaps.Count} folder(s)...";

        for (int i = 0; i < gaps.Count; i++)
        {
            StatusMessage = $"Filling gaps {i + 1} of {gaps.Count}: {gaps[i].DisplayName}";
            await ScrapeOneAsync(gaps[i]);
            await Task.Delay(200);
        }

        IsWorking = false;
        StatusMessage = $"Done. {gaps.Count(g => g.IsComplete)} of {gaps.Count} folder(s) now complete.";
    }

    [RelayCommand]
    public void RemoveItem(MovieFolderItem item)
    {
        if (item != null) RemoveItemInternal(item);
    }

    [RelayCommand]
    public void OpenFolder(MovieFolderItem item)
    {
        if (item == null || !Directory.Exists(item.FolderPath))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = item.FolderPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public void Save()
    {
        // Metadata is already saved per-movie when scraped (NFO + images written to disk).
        // This is a confirmation step.
        var saved = MovieFolders.Count(m => m.IsScraped);
        StatusMessage = $"Saved metadata for {saved} movie(s). Library is ready!";
    }

    private MovieFolderItem CreateMovieItem(string folderPath)
    {
        var folderName = Path.GetFileName(folderPath);
        var isShowRoot = IsTvShowRoot(folderPath);          // multi-season layout
        var isFlatTv   = !isShowRoot && IsFlatTvSeasonFolder(folderPath);
        var isTvShow   = isShowRoot || isFlatTv;

        string title;
        int year;
        bool hasNfo, hasPoster, hasFanart, hasActorPhotos;
        string? posterForCard;
        int seasonCount = 0;

        if (isTvShow)
        {
            // Show name preferred from episode filenames (more accurate than
            // folder names like "Breaking Bad Season 1 Complete"). Fall back to
            // cleaning the folder name if no parseable episode exists.
            title = isShowRoot ? folderName : ExtractShowNameFromFiles(folderPath, folderName);
            year = 0;
            seasonCount = isShowRoot
                ? CountSeasons(folderPath)
                : CountSeasonsInFlatFolder(folderPath);

            hasNfo    = File.Exists(Path.Combine(folderPath, "tvshow.nfo"));
            hasPoster = File.Exists(Path.Combine(folderPath, "poster.jpg"));
            hasFanart = File.Exists(Path.Combine(folderPath, "fanart.jpg"));
            var actorsDir = Path.Combine(folderPath, ".actors");
            hasActorPhotos = Directory.Exists(actorsDir)
                             && Directory.EnumerateFiles(actorsDir, "*.jpg").Any();
            posterForCard = hasPoster ? Path.Combine(folderPath, "poster.jpg") : null;
        }
        else
        {
            (title, year) = ExtractTitleAndYear(folderName);

            // Movie-shape asset detection (poster.jpg → "<basename>-poster.jpg")
            var existingPoster = Directory.GetFiles(folderPath, "*-poster.jpg").FirstOrDefault();
            var existingFanart = Directory.GetFiles(folderPath, "*-fanart.jpg").FirstOrDefault();
            hasNfo         = Directory.GetFiles(folderPath, "*.nfo").Any();
            var actorsDir  = Path.Combine(folderPath, ".actors");
            hasActorPhotos = Directory.Exists(actorsDir)
                             && Directory.EnumerateFiles(actorsDir, "*.jpg").Any();
            hasPoster = existingPoster != null;
            hasFanart = existingFanart != null;
            posterForCard = existingPoster;
        }

        var isComplete = hasNfo && hasPoster && hasFanart && hasActorPhotos;

        string statusText;
        if (isTvShow)
        {
            // Show "Complete" / "Missing: …" plus the season count for context.
            var verify = BuildVerifyStatus(hasNfo, hasPoster, hasFanart, hasActorPhotos);
            var seasonLabel = seasonCount > 0
                ? $"TV · {seasonCount} season{(seasonCount == 1 ? "" : "s")}"
                : "TV";
            statusText = $"{seasonLabel} · {verify}";
        }
        else
        {
            statusText = BuildVerifyStatus(hasNfo, hasPoster, hasFanart, hasActorPhotos);
        }

        return new MovieFolderItem
        {
            FolderPath = folderPath,
            MovieTitle = title,
            Year = year,
            IsTvShow = isTvShow,
            Status = statusText,
            IsScraped = isComplete,
            PosterPath = posterForCard,
            HasNfo = hasNfo,
            HasPoster = hasPoster,
            HasFanart = hasFanart,
            HasActorPhotos = hasActorPhotos,
            IsSelected = false
        };
    }

    /// <summary>Counts "Season XX" subfolders inside a TV show root.</summary>
    private static int CountSeasons(string showFolder)
    {
        if (!Directory.Exists(showFolder)) return 0;
        try
        {
            int count = 0;
            foreach (var sub in Directory.EnumerateDirectories(showFolder))
            {
                if (LooksLikeSeasonFolder(Path.GetFileName(sub))) count++;
            }
            return count;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Counts the distinct season numbers found by parsing the SxxExx pattern
    /// out of episode filenames in a flat TV folder. Typically returns 1.
    /// </summary>
    private static int CountSeasonsInFlatFolder(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(f => Utilities.FileFormatValidator.IsVideoFile(f))
                .Select(f => Utilities.RegexPatterns.ParseTvEpisode(Path.GetFileName(f)))
                .Where(p => p != null)
                .Select(p => p!.Season)
                .Distinct()
                .Count();
        }
        catch { return 0; }
    }

    /// <summary>
    /// Extracts the show name from episode files in a flat TV folder by parsing
    /// the SxxExx pattern out of each filename. Picks the most common parsed
    /// show name so a single oddly-named file doesn't skew the result.
    /// Falls back to <paramref name="folderName"/> when no episode can be parsed.
    /// </summary>
    private static string ExtractShowNameFromFiles(string folder, string folderName)
    {
        try
        {
            var names = Directory.EnumerateFiles(folder)
                .Where(f => Utilities.FileFormatValidator.IsVideoFile(f))
                .Select(f => Utilities.RegexPatterns.ParseTvEpisode(Path.GetFileName(f)))
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p!.ShowName))
                .Select(p => p!.ShowName)
                .ToList();

            if (names.Count == 0) return folderName;

            // Most-frequent wins.
            return names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(g => g.Count())
                        .First().Key;
        }
        catch { return folderName; }
    }

    /// <summary>
    /// Builds the per-row status text from the asset-presence flags. "Complete"
    /// when everything is there; "Missing: poster · fanart" when something isn't;
    /// "Ready" if nothing has been scraped yet.
    /// </summary>
    private static string BuildVerifyStatus(bool nfo, bool poster, bool fanart, bool actors)
    {
        // Nothing scraped at all → standard Ready state, not a "missing" warning.
        if (!nfo && !poster && !fanart && !actors) return "Ready";

        var missing = new List<string>();
        if (!nfo)    missing.Add("NFO");
        if (!poster) missing.Add("poster");
        if (!fanart) missing.Add("fanart");
        if (!actors) missing.Add("actors");

        return missing.Count == 0 ? "Complete" : "Missing: " + string.Join(" · ", missing);
    }

    /// <summary>True when every item is selected. Drives the All toggle's pressed state.</summary>
    public bool IsAllSelected => MovieFolders.Count > 0 && MovieFolders.All(m => m.IsSelected);

    /// <summary>True when no item is selected. Drives the None toggle's pressed state.</summary>
    public bool IsNoneSelected => MovieFolders.Count == 0 || MovieFolders.All(m => !m.IsSelected);

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var m in MovieFolders)
        {
            if (m.IsSelected) m.IsSelected = false;
            m.IsSelected = true;
        }
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(IsNoneSelected));
    }

    [RelayCommand]
    public void SelectNone()
    {
        foreach (var m in MovieFolders)
        {
            if (!m.IsSelected) m.IsSelected = true;
            m.IsSelected = false;
        }
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(IsNoneSelected));
    }

    /// <summary>Manually adds a single folder to the scraping list.</summary>
    public void AddSingleFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;
        if (_allFolders.Any(m => string.Equals(m.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)))
            return;
        if (IsTvShowRoot(folderPath) || HasVideoFile(folderPath))
            AddItem(CreateMovieItem(folderPath));
    }

    public void ClearAll() => ClearItems();

    private void EnsureScraperService()
    {
        if (_scraperService == null)
        {
            var apiKey = _configService.GetApiKey() ?? string.Empty;
            var lang = _configService.GetScrapeLanguage();
            _scraperService = new ScraperService(apiKey, lang);
        }
    }

    private static bool HasVideoFile(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Any(f => Utilities.FileFormatValidator.IsVideoFile(f));
        }
        catch
        {
            return false;
        }
    }

    private static (string title, int year) ExtractTitleAndYear(string folderName)
    {
        var lastParenIdx = folderName.LastIndexOf('(');
        if (lastParenIdx > 0)
        {
            var title = folderName[..lastParenIdx].Trim();
            var yearStr = folderName[(lastParenIdx + 1)..].TrimEnd(')');
            if (int.TryParse(yearStr, out var year))
                return (title, year);
        }
        return (folderName, 0);
    }
}
