using System.Collections.ObjectModel;
using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using CineLibraryEssentials.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CineLibraryEssentials.ViewModels;

public partial class FileToFolderViewModel : ObservableObject
{
    private readonly FileToFolderService _folderService = new();
    private readonly RenameService _renameService = new();
    private readonly ConfigService _configService;
    private readonly WizardViewModel _parentViewModel;

    [ObservableProperty]
    private ObservableCollection<FileOperation> operationsPreview = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    /// <summary>
    /// How many files were left out of the list because they're already sitting
    /// in the folder Step 2 would move them to (e.g. a recursive Step 1 scan of a
    /// library that's already organized). Shown in the status line.
    /// </summary>
    private int _alreadyOrganizedCount;

    public FileToFolderViewModel(WizardViewModel parentViewModel, ConfigService? configService = null)
    {
        _parentViewModel = parentViewModel;
        _configService = configService ?? new ConfigService();

        // Track per-row selection so the master "select all" checkbox reflects
        // the true state (all / none / partial).
        OperationsPreview.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (FileOperation op in e.NewItems)
                    op.PropertyChanged += OnOperationPropertyChanged;
            if (e.OldItems != null)
                foreach (FileOperation op in e.OldItems)
                    op.PropertyChanged -= OnOperationPropertyChanged;
            RaiseSelectionState();
        };
    }

    private void OnOperationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileOperation.IsSelected))
        {
            RaiseSelectionState();
            UpdateStatusMessage();
        }
    }

    private void RaiseSelectionState()
    {
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(IsNoneSelected));
    }

    /// <summary>True when every row is selected. Drives the master checkbox's checked state.</summary>
    public bool IsAllSelected => OperationsPreview.Count > 0 && OperationsPreview.All(op => op.IsSelected);

    /// <summary>True when no row is selected. Drives the master checkbox's unchecked state.</summary>
    public bool IsNoneSelected => OperationsPreview.Count == 0 || OperationsPreview.All(op => !op.IsSelected);

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var op in OperationsPreview) op.IsSelected = true;
        RaiseSelectionState();
        UpdateStatusMessage();
    }

    [RelayCommand]
    public void SelectNone()
    {
        foreach (var op in OperationsPreview) op.IsSelected = false;
        RaiseSelectionState();
        UpdateStatusMessage();
    }

    /// <summary>
    /// Auto-populates the operation list from Step 1's renamed previews when the
    /// user arrives from Step 1.
    /// </summary>
    public void RefreshFromRenameStep() => LoadPreview();

    [RelayCommand]
    public void LoadPreview()
    {
        if (_parentViewModel.RenamePreview.Count == 0 && OperationsPreview.Count == 0)
            return;

        // If we have rename-step data, build operations from it (replacing only those rows,
        // preserving any manually-added rows that weren't in RenamePreview)
        if (_parentViewModel.RenamePreview.Count > 0)
        {
            // Destinations are computed IN PLACE (each file's own directory).
            var operations = _renameService.CreateFileOperations(
                _parentViewModel.SelectedSourceFolder ?? string.Empty,
                _parentViewModel.RenamePreview);

            // Replace existing rename-step rows but keep manually-added ones
            var manuallyAdded = OperationsPreview
                .Where(op => !_parentViewModel.RenamePreview.Any(rp =>
                    string.Equals(rp.OriginalFilePath, op.OriginalFilePath, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            OperationsPreview.Clear();
            _alreadyOrganizedCount = 0;
            foreach (var op in operations)
            {
                // Skip files already sitting in their own correctly-named folder —
                // Step 2 only wraps LOOSE files, in place.
                if (IsAlreadyInPlace(op)) { _alreadyOrganizedCount++; continue; }

                op.IsSelected = true;
                OperationsPreview.Add(op);
            }
            foreach (var op in manuallyAdded)
            {
                if (IsAlreadyInPlace(op)) { _alreadyOrganizedCount++; continue; }
                OperationsPreview.Add(op);
            }
        }

        UpdateStatusMessage();
    }

    /// <summary>
    /// True when the file already sits in its own correctly-named folder, so Step 2
    /// has nothing to do for it. This is independent of the output folder — a file is
    /// "already organized" purely based on where it lives now:
    ///   • Movie: its parent folder name already equals the cleaned "Title (Year)".
    ///   • TV: its parent folder is a "Season XX" folder (the show/season layout).
    /// </summary>
    private static bool IsAlreadyInPlace(FileOperation op)
    {
        var srcDir = Path.GetDirectoryName(op.OriginalFilePath);
        if (string.IsNullOrEmpty(srcDir)) return false;
        var parentName = Path.GetFileName(srcDir.TrimEnd('\\'));
        if (string.IsNullOrEmpty(parentName)) return false;

        if (op.IsTvEpisode)
        {
            // Already in a "Season 01" (or "Season 1" / "Specials") folder.
            return System.Text.RegularExpressions.Regex.IsMatch(
                parentName, @"^(?:Season|Specials?)[\s_\.]?\d*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // Movie: parent folder name matches the cleaned "Title (Year)".
        var targetFolder = PathSanitizer.SanitizeFolderName(
            Path.GetFileNameWithoutExtension(op.FinalFileName));
        return string.Equals(
            PathSanitizer.SanitizeFolderName(parentName),
            targetFolder,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Computes the IN-PLACE destination: a folder created in the file's own
    /// current directory. No separate output folder is involved.
    ///   • Movie: &lt;fileDir&gt;/Title (Year)/
    ///   • TV:    &lt;fileDir&gt;/Show/Season XX/
    /// </summary>
    private static void RecomputeDestination(FileOperation op)
    {
        var baseDir = Path.GetDirectoryName(op.OriginalFilePath);
        if (string.IsNullOrEmpty(baseDir))
        {
            op.DestinationFolder = string.Empty;
            return;
        }

        if (op.IsTvEpisode)
        {
            var showFolder = PathSanitizer.SanitizeFolderName(op.ShowName);
            op.DestinationFolder = Path.Combine(baseDir, showFolder, $"Season {op.Season:D2}");
            return;
        }

        var folderName = Path.GetFileNameWithoutExtension(op.FinalFileName);
        if (string.IsNullOrWhiteSpace(folderName))
            folderName = Path.GetFileNameWithoutExtension(op.OriginalFileName);
        folderName = PathSanitizer.SanitizeFolderName(folderName);
        op.DestinationFolder = Path.Combine(baseDir, folderName);
    }

    /// <summary>
    /// Manually appends a list of file paths to the operations list. Useful when
    /// the user lands on Step 2 directly without renaming first.
    /// </summary>
    public void AddFiles(IEnumerable<string> filePaths)
    {
        _alreadyOrganizedCount = 0;
        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;
            if (!FileFormatValidator.IsVideoFile(path)) continue;
            // Dedupe
            if (OperationsPreview.Any(op => string.Equals(op.OriginalFilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var fileName = Path.GetFileName(path);
            var ext = Path.GetExtension(path);

            // TV detection — if the file is an episode, build a TV-shaped FileOperation
            // so RecomputeDestination puts it into Show/Season XX/.
            var tv = RegexPatterns.ParseTvEpisode(fileName);
            if (tv != null && !string.IsNullOrEmpty(tv.ShowName))
            {
                var tvName = PathSanitizer.SanitizeFileName(
                    RenameService.BuildTvFileName(tv.ShowName, tv.Season, tv.Episode, tv.EpisodeTitle)) + ext;
                var tvOp = new FileOperation
                {
                    OriginalFilePath = path,
                    OriginalFileName = fileName,
                    CleanedTitle = tv.ShowName,
                    Confidence = tv.Confidence,
                    FinalFileName = tvName,
                    IsSelected = true,
                    ShowName = tv.ShowName,
                    Season = tv.Season,
                    Episode = tv.Episode,
                    EpisodeTitle = tv.EpisodeTitle,
                };
                RecomputeDestination(tvOp);
                // Skip files already sitting in a Season folder of their own show.
                if (IsAlreadyInPlace(tvOp)) { _alreadyOrganizedCount++; continue; }
                OperationsPreview.Add(tvOp);
                continue;
            }

            // Movie path
            var parsed = RegexPatterns.ParseFilename(fileName);
            var formatted = RenameService.ApplyTemplate(parsed.Title, parsed.Year, _configService.GetLastTemplate());
            var cleanedName = PathSanitizer.SanitizeFileName(formatted) + ext;

            var op = new FileOperation
            {
                OriginalFilePath = path,
                OriginalFileName = fileName,
                CleanedTitle = parsed.Title,
                Year = parsed.Year,
                Confidence = parsed.Confidence,
                FinalFileName = cleanedName,
                IsSelected = true,
            };
            RecomputeDestination(op);
            // Skip files already in their own correctly-named folder.
            if (IsAlreadyInPlace(op)) { _alreadyOrganizedCount++; continue; }
            OperationsPreview.Add(op);
        }
        UpdateStatusMessage();
    }

    /// <summary>Recursively pulls all videos from a folder and its subfolders.</summary>
    public void AddFolder(string folderPath, bool recursive = true)
    {
        if (!Directory.Exists(folderPath)) return;
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(folderPath, "*", option)
            .Where(FileFormatValidator.IsVideoFile);
        AddFiles(files);
    }

    public void RemoveOperation(FileOperation op)
    {
        if (op != null) OperationsPreview.Remove(op);
        UpdateStatusMessage();
    }

    public void ClearAll()
    {
        OperationsPreview.Clear();
        UpdateStatusMessage();
    }

    private void UpdateStatusMessage()
    {
        // Note when files were hidden because they're already organized — explains
        // why the list may be shorter than what Step 1 showed.
        var organizedNote = _alreadyOrganizedCount > 0
            ? $" · {_alreadyOrganizedCount} already in folders (skipped)"
            : string.Empty;

        if (OperationsPreview.Count == 0)
        {
            StatusMessage = _alreadyOrganizedCount > 0
                ? $"All {_alreadyOrganizedCount} file(s) are already in their own folders — nothing to organize here."
                : "No loose files — drop a folder, click + Add Files / + Add Folder, or come back from Step 1.";
        }
        else
        {
            var selected = OperationsPreview.Count(op => op.IsSelected);
            StatusMessage = $"{selected} of {OperationsPreview.Count} loose file(s) will each get their own folder{organizedNote}";
        }
    }

    /// <summary>
    /// Executes the move/rename for selected operations. Returns true on full success.
    /// </summary>
    public async Task<ProcessingResult> RunAsync()
    {
        var toRun = OperationsPreview.Where(op => op.IsSelected).ToList();
        if (toRun.Count == 0)
            return new ProcessingResult { Success = true, Message = "No files selected." };

        IsLoading = true;
        try
        {
            // Honour the same "Clean metadata" preference that Step 1 uses.
            // This way users who skip Step 1's "Rename Selected" still get clean files.
            var cleanMetadata = _configService.GetCleanEmbeddedMetadata();

            var result = await _folderService.OrganizeFilesAsync(toRun, cleanMetadata);
            if (result.Success)
            {
                _parentViewModel.SetFileOperations(toRun);
                // Files were wrapped in place — tell Step 3 to look at the source
                // root so it can find the newly-created movie folders.
                _parentViewModel.SelectedOutputFolder = _parentViewModel.SelectedSourceFolder ?? string.Empty;
                var suffix = cleanMetadata ? " (with metadata cleanup)" : string.Empty;
                // result.Message already includes merge counts ("N organized · M merged…")
                StatusMessage = $"✓ {result.Message}{suffix}";
            }
            else
            {
                StatusMessage = $"⚠ {result.Errors.Count} error(s). Some files may not have been moved.";
            }
            return result;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
