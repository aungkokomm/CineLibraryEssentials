using System.Collections.ObjectModel;
using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CineLibraryEssentials.ViewModels;

public partial class FileToFolderViewModel : ObservableObject
{
    private readonly FileToFolderService _folderService = new();
    private readonly RenameService _renameService = new();
    private readonly WizardViewModel _parentViewModel;

    [ObservableProperty]
    private string outputFolderPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FileOperation> operationsPreview = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public FileToFolderViewModel(WizardViewModel parentViewModel)
    {
        _parentViewModel = parentViewModel;
    }

    /// <summary>
    /// Auto-populates the operation list from Step 1's renamed previews.
    /// Defaults the output folder to the source folder so the preview is meaningful
    /// the moment the user lands on Step 2.
    /// </summary>
    public void RefreshFromRenameStep()
    {
        // Default output folder to source so the preview always shows a real destination
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
        if (_parentViewModel.RenamePreview.Count == 0)
            return;

        // Use the source folder Step 1 selected; output folder is set on this VM
        var operations = _renameService.CreateFileOperations(
            _parentViewModel.SelectedSourceFolder ?? string.Empty,
            _parentViewModel.RenamePreview,
            string.IsNullOrEmpty(OutputFolderPath)
                ? "(awaiting output folder)"
                : OutputFolderPath);

        OperationsPreview.Clear();
        foreach (var op in operations)
        {
            op.IsSelected = true;  // all checked by default
            OperationsPreview.Add(op);
        }

        StatusMessage = string.IsNullOrEmpty(OutputFolderPath)
            ? "Select an output folder above to enable Run."
            : $"{OperationsPreview.Count} file(s) ready to organize into {OutputFolderPath}";
    }

    partial void OnOutputFolderPathChanged(string value)
    {
        // Keep the preview's destination paths in sync as the user changes output folder
        if (OperationsPreview.Count > 0)
            LoadPreview();
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
            var result = await _folderService.OrganizeFilesAsync(toRun);
            if (result.Success)
            {
                _parentViewModel.SetFileOperations(toRun);
                _parentViewModel.SelectedOutputFolder = OutputFolderPath;
                StatusMessage = $"✓ Organized {toRun.Count} file(s) into {OutputFolderPath}";
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
