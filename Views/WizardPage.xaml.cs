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

        // Each step's footer "← Back" button routes here.
        RenameStepView.BackRequested += (_, _) => GoBack();
        FileToFolderStepView.BackRequested += (_, _) => GoBack();
        ScrapingStepView.BackRequested += (_, _) => GoBack();

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

    private void GoBack()
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

    // Per-step identity colors: green → Clean Names, amber → Organize, orange → Scrape.
    private static readonly Color Step1Color = Color.FromArgb(0xFF, 0x16, 0xA3, 0x4A); // green
    private static readonly Color Step2Color = Color.FromArgb(0xFF, 0xD9, 0xA4, 0x06); // amber
    private static readonly Color Step3Color = Color.FromArgb(0xFF, 0xEA, 0x58, 0x0C); // orange

    private void UpdatePillStates()
    {
        var step = _viewModel.CurrentStep;
        // A pill is "done" once you've moved past it — show a check + keep its
        // color tint so completed steps read as finished, not just inactive.
        StyleStepButton(Step1Button, Step1Bullet, Step1Color, isActive: step == 0, isDone: step > 0,
            activeTextDark: false, bulletText: "1");
        StyleStepButton(Step2Button, Step2Bullet, Step2Color, isActive: step == 1, isDone: step > 1,
            activeTextDark: true, bulletText: "2");   // amber needs dark text for legibility
        StyleStepButton(Step3Button, Step3Bullet, Step3Color, isActive: step == 2, isDone: false,
            activeTextDark: false, bulletText: "3");
    }

    private void StyleStepButton(
        Button btn, Border? bullet, Color stepColor,
        bool isActive, bool isDone, bool activeTextDark, string bulletText)
    {
        var bulletLabel = bullet?.Child as TextBlock;

        if (isActive)
        {
            // Filled pill in the step's color.
            btn.Background = new SolidColorBrush(stepColor);
            var textColor = activeTextDark
                ? Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A)
                : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            btn.Foreground = new SolidColorBrush(textColor);
            if (bullet != null)
                bullet.Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
            if (bulletLabel != null)
            {
                bulletLabel.Text = bulletText;
                bulletLabel.Foreground = new SolidColorBrush(textColor);
            }
        }
        else
        {
            // Inactive / done: transparent pill, but the bullet keeps the step
            // color so each step has a consistent identity. Done steps show ✓.
            btn.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            btn.Foreground = (SolidColorBrush)App.Current.Resources["TextFillColorPrimaryBrush"];
            if (bullet != null)
            {
                // Tint the bullet with a soft version of the step color.
                bullet.Background = new SolidColorBrush(
                    Color.FromArgb(isDone ? (byte)0xFF : (byte)0x33, stepColor.R, stepColor.G, stepColor.B));
            }
            if (bulletLabel != null)
            {
                bulletLabel.Text = isDone ? "✓" : bulletText;   // ✓ for completed
                bulletLabel.Foreground = new SolidColorBrush(
                    isDone ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
                           : (App.Current.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush)?.Color
                             ?? Color.FromArgb(0xFF, 0, 0, 0));
            }
        }
    }

}
