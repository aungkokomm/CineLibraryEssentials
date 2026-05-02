using CineLibraryEssentials.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CineLibraryEssentials.Views;

public sealed partial class WizardPage : Page
{
    private WizardViewModel _viewModel;
    private RenameViewModel _renameViewModel;
    private FileToFolderViewModel _fileToFolderViewModel;
    private ScrapingViewModel _scrapingViewModel;

    public WizardPage()
    {
        InitializeComponent();

        _viewModel = new WizardViewModel();
        DataContext = _viewModel;

        _renameViewModel = new RenameViewModel(_viewModel);
        _fileToFolderViewModel = new FileToFolderViewModel(_viewModel);
        _scrapingViewModel = new ScrapingViewModel(_viewModel);

        RenameStepView.SetViewModel(_renameViewModel);
        FileToFolderStepView.SetViewModel(_fileToFolderViewModel);
        ScrapingStepView.SetViewModel(_scrapingViewModel);

        // After "Run File to Folder" completes, advance to Step 3 automatically
        FileToFolderStepView.OperationCompleted += (_, _) => GoToStep(2);

        GoToStep(0);
    }

    // -----------------------------------------------------------------
    //  Step pill clicks (with validation when moving forward)
    // -----------------------------------------------------------------

    private async void OnStep1Click(object sender, RoutedEventArgs e) => GoToStep(0);

    private async void OnStep2Click(object sender, RoutedEventArgs e)
    {
        // Going from Step 1 → Step 2 needs the rename data validated
        if (_viewModel.CurrentStep == 0)
        {
            if (!await ValidateRenameStepAsync()) return;
        }
        GoToStep(1);
    }

    private async void OnStep3Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentStep == 0)
        {
            if (!await ValidateRenameStepAsync()) return;
        }
        // Step 3 normally flows from Step 2 via OperationCompleted, but allow direct nav too
        GoToStep(2);
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentStep > 0) GoToStep(_viewModel.CurrentStep - 1);
    }

    private async void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();
    }

    // -----------------------------------------------------------------
    //  Step navigation
    // -----------------------------------------------------------------

    private void GoToStep(int step)
    {
        _viewModel.CurrentStep = step;

        // Toggle visibility
        RenameStepView.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        FileToFolderStepView.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        ScrapingStepView.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;

        UpdatePillStates();
        OnEnterStep(step);
    }

    private void OnEnterStep(int step)
    {
        if (step == 1) FileToFolderStepView.RefreshFromRenameStep();
        else if (step == 2) ScrapingStepView.RefreshFromOrganized();
    }

    private void UpdatePillStates()
    {
        StyleStepButton(Step1Button, Step1Bullet, _viewModel.CurrentStep == 0);
        StyleStepButton(Step2Button, Step2Bullet, _viewModel.CurrentStep == 1);
        StyleStepButton(Step3Button, Step3Bullet, _viewModel.CurrentStep == 2);

        PreviousButton.IsEnabled = _viewModel.CurrentStep > 0;
    }

    private void StyleStepButton(Button btn, Border? bullet, bool isActive)
    {
        if (isActive)
        {
            btn.Background = (SolidColorBrush)App.Current.Resources["AccentFillColorDefaultBrush"];
            btn.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            if (bullet != null)
                bullet.Background = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));
        }
        else
        {
            btn.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            btn.Foreground = (SolidColorBrush)App.Current.Resources["TextFillColorPrimaryBrush"];
            if (bullet != null)
                bullet.Background = (SolidColorBrush)App.Current.Resources["ControlFillColorTertiaryBrush"];
        }
    }

    // -----------------------------------------------------------------
    //  Step 1 → Step 2 validation
    // -----------------------------------------------------------------

    private async Task<bool> ValidateRenameStepAsync()
    {
        if (string.IsNullOrEmpty(_renameViewModel.SourceFolderPath))
        {
            await ShowErrorAsync("Please select a source folder.");
            return false;
        }

        var selected = _renameViewModel.AllPreviews.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            await ShowErrorAsync("No files selected.");
            return false;
        }

        if (_renameViewModel.DuplicateCount > 0)
        {
            await ShowErrorAsync($"Resolve {_renameViewModel.DuplicateCount} duplicate name(s) before continuing.");
            return false;
        }

        _viewModel.SelectedSourceFolder = _renameViewModel.SourceFolderPath;
        _viewModel.SetRenamePreview(selected);
        return true;
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "CineLibrary",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
