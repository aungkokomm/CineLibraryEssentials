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

        // Main rating
        SelectComboByTag(RatingCombo, _config.GetPreferImdbRating() ? "imdb" : "tmdb");
        EpisodeTitlesToggle.IsOn = _config.GetAddEpisodeTitles();

        // Words to always remove
        RemoveWordsBox.Text = string.Join(Environment.NewLine, _config.GetCustomRemoveWords());

        _isLoading = false;
    }

    /// <summary>True when the "Words to always remove" list was changed and saved,
    /// so the caller can re-clean the files Step 1 is showing.</summary>
    public bool RemoveWordsChanged { get; private set; }

    private void OnDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        var words = RemoveWordsBox.Text
            .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => w.Length > 0)
            .ToList();

        var before = _config.GetCustomRemoveWords();
        if (words.Count == before.Count
            && words.Zip(before).All(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)))
            return;

        _config.SetCustomRemoveWords(words);
        Utilities.RegexPatterns.SetCustomRemoveWords(_config.GetCustomRemoveWords());
        RemoveWordsChanged = true;
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

    private void OnRatingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (RatingCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string source)
            _config.SetPreferImdbRating(source == "imdb");
    }

    private void OnEpisodeTitlesToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_isLoading) return;
        _config.SetAddEpisodeTitles(EpisodeTitlesToggle.IsOn);
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (LanguageCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string code)
            _config.SetScrapeLanguage(code);
    }
}
