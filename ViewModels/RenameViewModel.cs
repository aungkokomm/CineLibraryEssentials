using System.Collections.ObjectModel;
using System.ComponentModel;
using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using CineLibraryEssentials.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace CineLibraryEssentials.ViewModels;

public partial class RenameViewModel : ObservableObject
{
    private readonly RenameService _renameService = new();
    private readonly ConfigService _configService;
    private readonly UndoService _undoService = new();
    private readonly WizardViewModel _parentViewModel;
    private readonly DispatcherQueue _dispatcherQueue;

    public UndoService UndoService => _undoService;

    /// <summary>Master list of all loaded files (unfiltered).</summary>
    public ObservableCollection<FilePreview> AllPreviews { get; } = new();

    /// <summary>Filtered/searched view exposed to the UI.</summary>
    [ObservableProperty]
    private ObservableCollection<FilePreview> filePreviews = new();

    [ObservableProperty]
    private string sourceFolderPath = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string confidenceFilter = "All";

    [ObservableProperty]
    private string findText = string.Empty;

    [ObservableProperty]
    private string replaceText = string.Empty;

    [ObservableProperty]
    private bool isRecursive;

    [ObservableProperty]
    private bool renameParentFolder;

    [ObservableProperty]
    private string outputTemplate = RenameService.TemplatePlex;

    [ObservableProperty]
    private bool cleanEmbeddedMetadata;

    /// <summary>"Auto", "Movies", or "TvShows" — wizard mode selector at the top of Step 1.</summary>
    [ObservableProperty]
    private string wizardMode = "Auto";

    /// <summary>
    /// When true, rows where CleanedName already matches OriginalName are hidden.
    /// Defaults to ON so users only see files that actually need renaming.
    /// </summary>
    [ObservableProperty]
    private bool hideUnchanged = true;

    [ObservableProperty]
    private string sortColumn = "Confidence";

    [ObservableProperty]
    private bool sortDescending = true;

    // ---- Stats ----
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int reviewedCount;
    [ObservableProperty] private int warningCount;
    [ObservableProperty] private int duplicateCount;
    [ObservableProperty] private int selectedCount;
    [ObservableProperty] private int alreadyCleanCount;

    [ObservableProperty]
    private string statsSummary = "No files loaded";

    public RenameViewModel(WizardViewModel parentViewModel, ConfigService? configService = null)
    {
        _parentViewModel = parentViewModel;
        _configService = configService ?? new ConfigService();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Restore persisted settings
        cleanEmbeddedMetadata = _configService.GetCleanEmbeddedMetadata();
        outputTemplate = _configService.GetLastTemplate();
        isRecursive = _configService.GetRecursiveScanDefault();
        wizardMode = _configService.GetWizardMode();
        var (col, desc) = _configService.GetStep1Sort();
        sortColumn = col;
        sortDescending = desc;
    }

    // -----------------------------------------------------------------
    //  Loading
    // -----------------------------------------------------------------

    /// <summary>
    /// On startup, re-open the most recently used source folder (if it still
    /// exists). Does nothing if there's no history or the folder is gone, and
    /// never overrides a folder the user has already picked this session.
    /// </summary>
    public async Task TryLoadLastFolderAsync()
    {
        if (!string.IsNullOrEmpty(SourceFolderPath)) return;  // user already picked one

        var recent = _configService.GetRecentSourceFolders();
        var last = recent.FirstOrDefault(Directory.Exists);
        if (string.IsNullOrEmpty(last)) return;

        await LoadFilesAsync(last);
    }

    public async Task LoadFilesAsync(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return;

        IsLoading = true;
        SourceFolderPath = folderPath;
        _configService.AddRecentSourceFolder(folderPath);

        var recursive = IsRecursive;
        var template = OutputTemplate;
        var mode = WizardMode switch
        {
            "Movies"  => RenameService.Mode.Movies,
            "TvShows" => RenameService.Mode.TvShows,
            _         => RenameService.Mode.Auto,
        };
        var previews = await Task.Run(() =>
            _renameService.AnalyzeFiles(folderPath, recursive, template, mode));

        _dispatcherQueue.TryEnqueue(() =>
        {
            // Detach old subscriptions, clear, re-load
            foreach (var p in AllPreviews) p.PropertyChanged -= OnPreviewPropertyChanged;
            AllPreviews.Clear();

            foreach (var p in previews)
            {
                p.PropertyChanged += OnPreviewPropertyChanged;
                AllPreviews.Add(p);
            }

            ValidateAll();
            ApplyFilter();
            IsLoading = false;
        });
    }

    // Reload when toggle / template changes (only if a folder is already loaded)
    partial void OnIsRecursiveChanged(bool value)
    {
        if (!string.IsNullOrEmpty(SourceFolderPath))
            _ = LoadFilesAsync(SourceFolderPath);
    }

    partial void OnOutputTemplateChanged(string value)
    {
        _configService.SetLastTemplate(value);
        // Re-apply template to all NON-reviewed rows in place (faster than re-scanning)
        if (AllPreviews.Count == 0) return;
        foreach (var p in AllPreviews)
        {
            if (p.IsReviewed) continue;
            var ext = Path.GetExtension(p.OriginalName);
            var formatted = RenameService.ApplyTemplate(
                Path.GetFileNameWithoutExtension(p.CleanedName)
                    .Replace($" ({p.Year})", string.Empty)
                    .Replace($"{p.Year} - ", string.Empty)
                    .Trim(),
                p.Year,
                value);
            p.CleanedName = PathSanitizer.SanitizeFileName(formatted) + ext;
        }
        ValidateAll();
        UpdateStats();
    }

    private void OnPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When the user edits a name or toggles selection, re-validate + recompute stats.
        if (e.PropertyName == nameof(FilePreview.CleanedName)
            || e.PropertyName == nameof(FilePreview.IsSelected)
            || e.PropertyName == nameof(FilePreview.IsReviewed))
        {
            ValidateAll();
            UpdateStats();
            OnPropertyChanged(nameof(PendingRenameCount));
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(IsNoneSelected));
        }
    }

    // -----------------------------------------------------------------
    //  Filtering / search
    // -----------------------------------------------------------------

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnConfidenceFilterChanged(string value) => ApplyFilter();
    partial void OnHideUnchangedChanged(bool value) => ApplyFilter();
    partial void OnSortColumnChanged(string value)
    {
        _configService.SetStep1Sort(value, SortDescending);
        ApplyFilter();
    }
    partial void OnSortDescendingChanged(bool value)
    {
        _configService.SetStep1Sort(SortColumn, value);
        ApplyFilter();
    }
    partial void OnWizardModeChanged(string value)
    {
        _configService.SetWizardMode(value);
        // Re-scan the current folder so the row list reflects the new mode
        // (Movies-mode strips TV warnings, TvShows-mode flags non-S/E rows).
        if (!string.IsNullOrEmpty(SourceFolderPath))
            _ = LoadFilesAsync(SourceFolderPath);
    }

    partial void OnCleanEmbeddedMetadataChanged(bool value)
        => _configService.SetCleanEmbeddedMetadata(value);

    private void ApplyFilter()
    {
        var filtered = AllPreviews.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            filtered = filtered.Where(p =>
                p.OriginalName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.CleanedName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        filtered = ConfidenceFilter switch
        {
            "Low" => filtered.Where(p => p.Confidence < 0.50),
            "Medium" => filtered.Where(p => p.Confidence >= 0.50 && p.Confidence < 0.80),
            "High" => filtered.Where(p => p.Confidence >= 0.80),
            "Warnings" => filtered.Where(p => p.HasWarning),
            "Needs renaming" => filtered.Where(p =>
                !string.Equals(p.OriginalName, p.CleanedName, StringComparison.Ordinal)),
            _ => filtered
        };

        // "Hide unchanged" toggle (orthogonal to the dropdown filter)
        if (HideUnchanged)
        {
            filtered = filtered.Where(p =>
                !string.Equals(p.OriginalName, p.CleanedName, StringComparison.Ordinal));
        }

        // Apply sorting
        IOrderedEnumerable<FilePreview> sorted = SortColumn switch
        {
            "Original" => SortDescending
                ? filtered.OrderByDescending(p => p.OriginalName, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(p => p.OriginalName, StringComparer.OrdinalIgnoreCase),
            "Cleaned" => SortDescending
                ? filtered.OrderByDescending(p => p.CleanedName, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(p => p.CleanedName, StringComparer.OrdinalIgnoreCase),
            "Size" => SortDescending
                ? filtered.OrderByDescending(p => p.FileSizeBytes)
                : filtered.OrderBy(p => p.FileSizeBytes),
            _ => SortDescending
                ? filtered.OrderByDescending(p => p.Confidence)
                : filtered.OrderBy(p => p.Confidence),
        };

        FilePreviews = new ObservableCollection<FilePreview>(sorted);
        UpdateStats();
    }

    /// <summary>Toggles the sort direction if the same column is clicked, else switches column.</summary>
    public void ToggleSort(string column)
    {
        if (string.Equals(column, SortColumn, StringComparison.OrdinalIgnoreCase))
            SortDescending = !SortDescending;
        else
        {
            SortColumn = column;
            SortDescending = column == "Confidence" || column == "Size";  // sensible defaults
        }
    }

    // -----------------------------------------------------------------
    //  Validation (duplicate names, invalid chars, missing year)
    // -----------------------------------------------------------------

    private void ValidateAll()
    {
        var nameCounts = AllPreviews
            .Where(p => p.IsSelected)
            .GroupBy(p => p.CleanedName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var p in AllPreviews)
        {
            var warnings = new List<string>();

            if (string.IsNullOrWhiteSpace(p.CleanedName))
                warnings.Add("Name is empty");

            if (p.CleanedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                warnings.Add("Contains invalid filename characters");

            if (p.Year == 0 && p.IsSelected)
                warnings.Add("No year detected");

            // Implausible year
            if (p.Year > 0 && (p.Year < 1900 || p.Year > DateTime.Now.Year + 2))
                warnings.Add($"Implausible year ({p.Year})");

            // TV episode detection
            if (p.IsTvEpisode)
                warnings.Add("Looks like a TV episode, not a movie");

            // Filename too long (Windows MAX_PATH risk when combined with output folder)
            if (p.CleanedName.Length > 200)
                warnings.Add($"Filename is too long ({p.CleanedName.Length} chars)");

            bool isDup = p.IsSelected
                && !string.IsNullOrWhiteSpace(p.CleanedName)
                && nameCounts.TryGetValue(p.CleanedName, out var count)
                && count > 1;

            if (isDup)
                warnings.Add("Duplicate name");

            p.IsDuplicate = isDup;
            p.HasWarning = warnings.Count > 0;
            p.WarningMessage = string.Join(" • ", warnings);
        }
    }

    private void UpdateStats()
    {
        TotalCount = AllPreviews.Count;
        ReviewedCount = AllPreviews.Count(p => p.IsReviewed);
        WarningCount = AllPreviews.Count(p => p.HasWarning);
        DuplicateCount = AllPreviews.Count(p => p.IsDuplicate);
        SelectedCount = AllPreviews.Count(p => p.IsSelected);
        AlreadyCleanCount = AllPreviews.Count(p =>
            string.Equals(p.OriginalName, p.CleanedName, StringComparison.Ordinal));

        if (AllPreviews.Count == 0)
        {
            StatsSummary = "No files loaded";
            return;
        }

        var parts = new List<string> { $"{TotalCount} file{(TotalCount == 1 ? "" : "s")}" };

        if (HideUnchanged && AlreadyCleanCount > 0)
            parts.Add($"{AlreadyCleanCount} already clean (hidden)");

        parts.Add($"{SelectedCount} selected");

        if (WarningCount > 0)
            parts.Add($"{WarningCount} warning{(WarningCount == 1 ? "" : "s")}");

        if (DuplicateCount > 0)
            parts.Add($"{DuplicateCount} duplicate{(DuplicateCount == 1 ? "" : "s")}");

        StatsSummary = string.Join(" · ", parts);
    }

    // -----------------------------------------------------------------
    //  Bulk operations
    // -----------------------------------------------------------------

    [RelayCommand]
    public void SelectAll() => SetAllSelection(true);

    [RelayCommand]
    public void SelectNone() => SetAllSelection(false);

    private void SetAllSelection(bool value)
    {
        foreach (var p in AllPreviews)
        {
            if (p.IsSelected == value) p.IsSelected = !value;
            p.IsSelected = value;
        }
        // Final notifications — guarantees the master toggles update even if the
        // intermediate property changes were debounced by a binding system.
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(IsNoneSelected));
        ApplyFilter();
    }

    [RelayCommand]
    public void ApplyTitleCase()
    {
        foreach (var p in FilePreviews.Where(x => x.IsSelected))
        {
            var ext = Path.GetExtension(p.CleanedName);
            var nameOnly = Path.GetFileNameWithoutExtension(p.CleanedName);
            p.CleanedName = RegexPatterns.SmartTitleCase(nameOnly) + ext;
        }
    }

    [RelayCommand]
    public void FindAndReplace()
    {
        if (string.IsNullOrEmpty(FindText)) return;
        foreach (var p in FilePreviews.Where(x => x.IsSelected))
        {
            p.CleanedName = p.CleanedName.Replace(FindText, ReplaceText ?? string.Empty);
        }
    }

    [RelayCommand]
    public void ResetCleaning()
    {
        foreach (var p in FilePreviews.Where(x => x.IsSelected))
        {
            var parsed = RegexPatterns.ParseFilename(p.OriginalName);
            var ext = Path.GetExtension(p.OriginalName);
            p.CleanedName = PathSanitizer.CreateMovieFileName(parsed.Title, parsed.Year, ext);
            p.Confidence = parsed.Confidence;
            p.Year = parsed.Year;
            p.IsReviewed = false;
        }
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SearchText = string.Empty;
    }

    /// <summary>
    /// Applies a TMDb match to the given preview row: updates the cleaned name to use
    /// the canonical title + year, marks the row as reviewed, and bumps confidence to 1.0.
    /// </summary>
    public void ApplyTmdbMatch(FilePreview preview, string canonicalTitle, int canonicalYear)
    {
        if (preview == null) return;
        var ext = Path.GetExtension(preview.OriginalName);
        var formatted = RenameService.ApplyTemplate(canonicalTitle, canonicalYear, OutputTemplate);
        preview.CleanedName = PathSanitizer.SanitizeFileName(formatted) + ext;
        preview.Year = canonicalYear;
        preview.Confidence = 1.0;
        preview.IsReviewed = true;
    }

    /// <summary>Removes a row from the master list (and from the filtered view).</summary>
    public void RemovePreview(FilePreview preview)
    {
        if (preview == null) return;
        preview.PropertyChanged -= OnPreviewPropertyChanged;
        AllPreviews.Remove(preview);
        FilePreviews.Remove(preview);
        ValidateAll();
        UpdateStats();
    }

    // -----------------------------------------------------------------
    //  Rename in place (destructive — actually renames files on disk)
    // -----------------------------------------------------------------

    /// <summary>How many SELECTED rows have a pending rename (CleanedName != OriginalName).</summary>
    public int PendingRenameCount => AllPreviews
        .Count(p => p.IsSelected
                 && !string.IsNullOrWhiteSpace(p.CleanedName)
                 && !string.Equals(p.OriginalName, p.CleanedName, StringComparison.Ordinal));

    /// <summary>True when every row is selected. Drives the "All" toggle's pressed state.</summary>
    public bool IsAllSelected => AllPreviews.Count > 0 && AllPreviews.All(p => p.IsSelected);

    /// <summary>True when no row is selected. Drives the "None" toggle's pressed state.</summary>
    public bool IsNoneSelected => AllPreviews.Count == 0 || AllPreviews.All(p => !p.IsSelected);

    /// <summary>
    /// Performs the actual rename on disk for selected rows whose CleanedName
    /// differs from OriginalName. Returns a result that callers can use to show
    /// success / error UI.
    /// </summary>
    public async Task<ProcessingResult> RenameInPlaceAsync()
    {
        // Include rows that need renaming. If "Clean metadata" is ticked, ALSO include
        // selected rows whose name is already clean — the user explicitly asked for the
        // metadata to be scrubbed, so the action must run even when no rename is pending.
        var toRename = AllPreviews
            .Where(p => p.IsSelected
                     && !string.IsNullOrWhiteSpace(p.CleanedName)
                     && (CleanEmbeddedMetadata
                         || !string.Equals(p.OriginalName, p.CleanedName, StringComparison.Ordinal)))
            .ToList();

        if (toRename.Count == 0)
            return new ProcessingResult { Success = true, Message = "No files selected." };

        IsLoading = true;
        try
        {
            var undoLog = new List<UndoService.RenameRecord>();
            var result = await _renameService.RenameInPlaceAsync(
                toRename,
                renameParentFolders: RenameParentFolder,
                sourceFolder: SourceFolderPath,
                cleanEmbeddedMetadata: CleanEmbeddedMetadata,
                undoLog: undoLog);

            // Push the batch onto the undo stack so the UI can offer a 30s undo toast
            if (undoLog.Count > 0)
                _undoService.Push(undoLog);

            // After rename, the originals on disk have changed: re-validate and refresh stats
            ValidateAll();
            UpdateStats();
            ApplyFilter();
            OnPropertyChanged(nameof(PendingRenameCount));

            return result;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Reverses the last rename batch.</summary>
    public (int succeeded, int failed) UndoLastRename()
    {
        var (ok, bad) = _undoService.UndoLast();
        // Reload the current source folder so previews reflect the on-disk state again
        if (ok > 0 && !string.IsNullOrEmpty(SourceFolderPath))
            _ = LoadFilesAsync(SourceFolderPath);
        return (ok, bad);
    }

    // -----------------------------------------------------------------
    //  Confirm step
    // -----------------------------------------------------------------

    public bool CanProceed => AllPreviews.Any(p => p.IsSelected) && DuplicateCount == 0;

    [RelayCommand]
    public void ConfirmAndProceed()
    {
        if (!CanProceed || string.IsNullOrEmpty(SourceFolderPath))
            return;

        _parentViewModel.SetRenamePreview(AllPreviews.Where(p => p.IsSelected).ToList());
        _parentViewModel.SelectedSourceFolder = SourceFolderPath;
        _parentViewModel.GoToNextStepCommand.Execute(null);
    }
}
