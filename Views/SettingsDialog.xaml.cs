using CineLibraryEssentials.Services;
using Microsoft.UI.Xaml.Controls;

namespace CineLibraryEssentials.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly ConfigService _config = new();

    // Set in the constructor before wiring change handlers so the initial Load
    // doesn't immediately re-write settings back to disk.
    private bool _isLoading = true;

    public SettingsDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        _isLoading = true;

        // Output template
        var template = _config.GetLastTemplate();
        SelectComboByTag(TemplateCombo, template);

        // Toggles
        RecursiveToggle.IsOn = _config.GetRecursiveScanDefault();
        CleanMetaToggle.IsOn = _config.GetCleanEmbeddedMetadata();
        AutoUpdateToggle.IsOn = _config.GetAutoCheckForUpdates();

        // Language
        SelectComboByTag(LanguageCombo, _config.GetScrapeLanguage());

        _isLoading = false;
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Tag?.ToString(), tag, System.StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = cbi;
                return;
            }
        }
        // Default to first item if no match
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    // ------- Change handlers (no-op while loading initial values) -------

    private void OnTemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (TemplateCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string t)
            _config.SetLastTemplate(t);
    }

    private void OnRecursiveToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_isLoading) return;
        _config.SetRecursiveScanDefault(RecursiveToggle.IsOn);
    }

    private void OnCleanMetaToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_isLoading) return;
        _config.SetCleanEmbeddedMetadata(CleanMetaToggle.IsOn);
    }

    private void OnAutoUpdateToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_isLoading) return;
        _config.SetAutoCheckForUpdates(AutoUpdateToggle.IsOn);
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (LanguageCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string code)
            _config.SetScrapeLanguage(code);
    }
}
