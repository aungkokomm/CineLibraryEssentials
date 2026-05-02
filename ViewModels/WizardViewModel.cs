using CineLibraryEssentials.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CineLibraryEssentials.ViewModels;

public partial class WizardViewModel : ObservableObject
{
    [ObservableProperty]
    private string selectedSourceFolder = string.Empty;

    [ObservableProperty]
    private string selectedOutputFolder = string.Empty;

    [ObservableProperty]
    private int currentStep = 0; // 0, 1, or 2

    [ObservableProperty]
    private bool isBusy = false;

    public List<FileOperation> AllFileOperations { get; set; } = new();
    public List<FilePreview> RenamePreview { get; set; } = new();

    [RelayCommand]
    public void GoToNextStep()
    {
        if (CurrentStep < 2)
            CurrentStep++;
    }

    [RelayCommand]
    public void GoToPreviousStep()
    {
        if (CurrentStep > 0)
            CurrentStep--;
    }

    public void SetRenamePreview(List<FilePreview> previews)
    {
        RenamePreview = previews;
    }

    public void SetFileOperations(List<FileOperation> operations)
    {
        AllFileOperations = operations;
    }

    [RelayCommand]
    public void CompleteWizard()
    {
        // Navigation logic will be handled by the View
        System.Diagnostics.Debug.WriteLine("Wizard completed");
    }
}
