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

    private void OnStep1Click(object sender, RoutedEventArgs e) => GoToStep(0);

    // Step 2 supports manual + Add Files / + Add Folder, so direct nav is fine.
    // We still hand off Step 1's data (best-effort, no blocking) so the user gets
    // a populated list if they came from Step 1 normally.
    private void OnStep2Click(object sender, RoutedEventArgs e)
    {
        TryHandOffRenameStepData();
        GoToStep(1);
    }

    // Step 3 supports manual + Add Folder + drag-drop. Direct nav is fine —
    // useful when the user has an already-organised library and just wants to scrape.
    private void OnStep3Click(object sender, RoutedEventArgs e) => GoToStep(2);

    /// <summary>
    /// Pushes Step 1's selected rename previews into the parent VM if they're valid.
    /// Silent on failure — if there's nothing to hand off, Step 2 will simply show
    /// its empty state and the user can use Add Files / Add Folder / drag-drop.
    /// </summary>
    private void TryHandOffRenameStepData()
    {
        if (_renameViewModel == null) return;
        if (string.IsNullOrEmpty(_renameViewModel.SourceFolderPath)) return;

        var selected = _renameViewModel.AllPreviews.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return;
        if (_renameViewModel.DuplicateCount > 0) return;

        _viewModel.SelectedSourceFolder = _renameViewModel.SourceFolderPath;
        _viewModel.SetRenamePreview(selected);
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

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog { XamlRoot = this.XamlRoot };
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

}
