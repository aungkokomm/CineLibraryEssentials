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
    private string outputFolderPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FileOperation> operationsPreview = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public FileToFolderViewModel(WizardViewModel parentViewModel, ConfigService? configService = null)
    {
        _parentViewModel = parentViewModel;
        _configService = configService ?? new ConfigService();
    }

    /// <summary>
    /// Auto-populates the operation list from Step 1's renamed previews.
    /// Defaults the output folder to the source folder so the preview is meaningful
    /// the moment the user lands on Step 2.
    /// </summary>
    public void RefreshFromRenameStep()
    {
        if (string.IsNullOrEmpty(OutputFolderPath)
            && !string.IsNullOrEmpty(_parentViewModel.SelectedSourceFolder))
        {
            OutputFolderPath = _parentViewModel.SelectedSourceFolder;
        }

        LoadPreview();
    }

    [RelayCommand]
    public void LoadPreview()
    {
        if (_parentViewModel.RenamePreview.Count == 0 && OperationsPreview.Count == 0)
            return;

        // If we have rename-step data, build operations from it (replacing only those rows,
        // preserving any manually-added rows that weren't in RenamePreview)
        if (_parentViewModel.RenamePreview.Count > 0)
        {
            var operations = _renameService.CreateFileOperations(
                _parentViewModel.SelectedSourceFolder ?? string.Empty,
                _parentViewModel.RenamePreview,
                string.IsNullOrEmpty(OutputFolderPath)
                    ? "(awaiting output folder)"
                    : OutputFolderPath);

            // Replace existing rename-step rows but keep manually-added ones
            var manuallyAdded = OperationsPreview
                .Where(op => !_parentViewModel.RenamePreview.Any(rp =>
                    string.Equals(rp.OriginalFilePath, op.OriginalFilePath, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            OperationsPreview.Clear();
            foreach (var op in operations)
            {
                op.IsSelected = true;
                OperationsPreview.Add(op);
            }
            foreach (var op in manuallyAdded)
            {
                // Recompute destination with the (possibly new) output folder
                RecomputeDestination(op);
                OperationsPreview.Add(op);
            }
        }
        else
        {
            // Recompute destinations for all manual operations
            foreach (var op in OperationsPreview)
                RecomputeDestination(op);
        }

        UpdateStatusMessage();
    }

    private void RecomputeDestination(FileOperation op)
    {
        if (string.IsNullOrEmpty(OutputFolderPath))
        {
            op.DestinationFolder = "(awaiting output folder)";
            return;
        }

        // TV path: <output>/Show Name/Season 01/. Preserves the Kodi layout
        // even after the user changes the output folder mid-session.
        if (op.IsTvEpisode)
        {
            var showFolder = PathSanitizer.SanitizeFolderName(op.ShowName);
            op.DestinationFolder = Path.Combine(OutputFolderPath, showFolder, $"Season {op.Season:D2}");
            return;
        }

        // Movie path: <output>/Title (Year)/
        var folderName = Path.GetFileNameWithoutExtension(op.FinalFileName);
        if (string.IsNullOrWhiteSpace(folderName))
            folderName = Path.GetFileNameWithoutExtension(op.OriginalFileName);
        folderName = PathSanitizer.SanitizeFolderName(folderName);
        op.DestinationFolder = Path.Combine(OutputFolderPath, folderName);
    }

    partial void OnOutputFolderPathChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            _configService.AddRecentOutputFolder(value);
        if (OperationsPreview.Count > 0)
            LoadPreview();
    }

    /// <summary>
    /// Manually appends a list of file paths to the operations list. Useful when
    /// the user lands on Step 2 directly without renaming first.
    /// </summary>
    public void AddFiles(IEnumerable<string> filePaths)
    {
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
                OperationsPreview.Add(tvOp);
                continue;
            }

            // Movie path (unchanged)
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
        if (OperationsPreview.Count == 0)
        {
            StatusMessage = "No files yet — drop a folder, click + Add Files, or come back from Step 1.";
        }
        else if (string.IsNullOrEmpty(OutputFolderPath))
        {
            StatusMessage = "Pick an output folder above to enable Run.";
        }
        else
        {
            var selected = OperationsPreview.Count(op => op.IsSelected);
            StatusMessage = $"{selected} of {OperationsPreview.Count} file(s) ready to organize into {OutputFolderPath}";
        }
    }

    /// <summary>
    /// Executes the move/rename for selected operations. Returns true on full success.
    /// </summary>
    public async Task<ProcessingResult> RunAsync()
    {
        var toRun = OperationsPreview.Where(op => op.IsSelected).ToList();
        if (toRun.Count == 0)
            return new ProcessingResult { Success = true, Message = "No operations selected." };

        if (string.IsNullOrEmpty(OutputFolderPath))
            return new ProcessingResult
            {
                Success = false,
                Message = "Output folder is not set.",
                Errors = { "Output folder is not set." }
            };

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
                _parentViewModel.SelectedOutputFolder = OutputFolderPath;
                var suffix = cleanMetadata ? " (with metadata cleanup)" : string.Empty;
                // result.Message already includes merge counts ("N organized · M merged…")
                StatusMessage = $"✓ {result.Message} → {OutputFolderPath}{suffix}";
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
