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
            // Skip if already added
            if (MovieFolders.Any(m => string.Equals(m.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)))
                continue;

            MovieFolders.Add(CreateMovieItem(folderPath));
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

        foreach (var movieFolder in EnumerateMovieFolders(rootPath))
        {
            if (MovieFolders.Any(m => string.Equals(m.FolderPath, movieFolder, StringComparison.OrdinalIgnoreCase)))
                continue;
            MovieFolders.Add(CreateMovieItem(movieFolder));
        }
    }

    /// <summary>
    /// Walks the directory tree from <paramref name="root"/> and yields every folder
    /// whose immediate files include a video. The root itself is checked too.
    /// </summary>
    public static IEnumerable<string> EnumerateMovieFolders(string root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) yield break;

        // Check root itself
        if (HasVideoFile(root)) yield return root;

        string[] subDirs;
        try { subDirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories); }
        catch { yield break; }

        foreach (var sub in subDirs)
        {
            if (HasVideoFile(sub)) yield return sub;
        }
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

        var progress = new Progress<string>(s =>
        {
            _dispatcherQueue.TryEnqueue(() => item.Status = s);
        });

        var (success, message) = await _scraperService!.ScrapeAndDownloadMetadataAsync(
            item.FolderPath, item.MovieTitle, item.Year, progress);

        item.IsScraping = false;
        if (success)
        {
            item.IsScraped = true;
            item.Status = "Complete";
            // Pick up the freshly-downloaded poster for the card thumbnail
            try
            {
                item.PosterPath = Directory.GetFiles(item.FolderPath, "*-poster.jpg").FirstOrDefault();
            }
            catch { }
        }
        else
        {
            item.Status = "Failed";
            item.ErrorMessage = message;
        }
    }

    /// <summary>
    /// Scrapes a single movie using a SPECIFIC TMDb id picked by the user from the
    /// search dialog. Skips the auto-search and goes straight to metadata download.
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

        var (success, message) = await _scraperService!.ScrapeByTmdbIdAsync(
            item.FolderPath, tmdbId, progress);

        item.IsScraping = false;
        if (success)
        {
            item.IsScraped = true;
            item.Status = "Complete";
            // Pick up the freshly-downloaded poster for the card thumbnail
            try
            {
                item.PosterPath = Directory.GetFiles(item.FolderPath, "*-poster.jpg").FirstOrDefault();
            }
            catch { }
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

        var selected = MovieFolders.Where(m => m.IsSelected && !m.IsScraped).ToList();
        for (int i = 0; i < selected.Count; i++)
        {
            StatusMessage = $"Scraping {i + 1} of {selected.Count}: {selected[i].DisplayName}";
            await ScrapeOneAsync(selected[i]);
            await Task.Delay(200);
        }

        IsWorking = false;
        StatusMessage = $"Done. {selected.Count(s => s.IsScraped)} of {selected.Count} succeeded.";
    }

    [RelayCommand]
    public void RemoveItem(MovieFolderItem item)
    {
        if (item != null)
            MovieFolders.Remove(item);
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
        var (title, year) = ExtractTitleAndYear(folderName);

        // Detect existing scraped state by looking for a poster + nfo
        var existingPoster = Directory.GetFiles(folderPath, "*-poster.jpg").FirstOrDefault();
        var hasNfo = Directory.GetFiles(folderPath, "*.nfo").Any();

        return new MovieFolderItem
        {
            FolderPath = folderPath,
            MovieTitle = title,
            Year = year,
            Status = (hasNfo && existingPoster != null) ? "Already scraped" : "Ready",
            IsScraped = hasNfo && existingPoster != null,
            PosterPath = existingPoster,
            // Always default to UNCHECKED — user picks which movies to scrape via the
            // checkbox or the All/None toggles. Avoids the "uncheck 100 items manually"
            // problem when most are already scraped.
            IsSelected = false
        };
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
        if (MovieFolders.Any(m => string.Equals(m.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)))
            return;
        if (HasVideoFile(folderPath))
            MovieFolders.Add(CreateMovieItem(folderPath));
    }

    public void ClearAll() => MovieFolders.Clear();

    private void EnsureScraperService()
    {
        if (_scraperService == null)
        {
            var apiKey = _configService.GetApiKey() ?? string.Empty;
            _scraperService = new ScraperService(apiKey);
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
