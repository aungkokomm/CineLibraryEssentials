using CineLibraryEssentials.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CineLibraryEssentials.Views;

/// <summary>
/// Step 3 "Bulk edit": one change (collection, age rating, genres, tags, lock)
/// applied to every checked card's NFO. Only what's ticked or filled in changes.
/// </summary>
public sealed partial class BulkEditDialog : ContentDialog
{
    private static readonly string[] AgeRatings =
        { "G", "PG", "PG-13", "R", "NC-17", "NR", "TV-Y", "TV-Y7", "TV-G", "TV-PG", "TV-14", "TV-MA" };

    private readonly List<string> _folders;
    private LibraryVocabulary _vocab = LibraryVocabulary.Empty;

    /// <summary>The outcome after Apply, or null when cancelled.</summary>
    public BulkNfoEdit.Result? Result { get; private set; }

    public BulkEditDialog(IReadOnlyList<string> folders, IEnumerable<string> libraryFolders)
    {
        InitializeComponent();
        _folders = folders.ToList();
        IntroText.Text = $"Applies to the {_folders.Count} checked item{(_folders.Count == 1 ? "" : "s")}. " +
                         "Only what you tick or fill in below changes; everything else stays as it is.";
        foreach (var r in AgeRatings) AgeRatingBox.Items.Add(r);
        _ = LoadSuggestionsAsync(libraryFolders.ToList());
    }

    private async Task LoadSuggestionsAsync(List<string> folders)
    {
        _vocab = await Task.Run(() => LibraryVocabulary.Collect(folders));
        AddGenresBox.Suggestions = RemoveGenresBox.Suggestions = _vocab.Genres;
        AddTagsBox.Suggestions = RemoveTagsBox.Suggestions = _vocab.Tags;
    }

    private void OnCollectionTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = LibraryVocabulary.Match(_vocab.Collections, sender.Text);
    }

    private void OnApplyClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var ageRating = (AgeRatingBox.SelectedItem as string) ?? AgeRatingBox.Text ?? string.Empty;
        var change = new NfoEditor.BulkChange
        {
            Collection = CollectionCheck.IsChecked == true ? CollectionBox.Text.Trim() : null,
            AgeRating = AgeRatingCheck.IsChecked == true ? ageRating.Trim() : null,
            AddGenres = AddGenresBox.Items,
            RemoveGenres = RemoveGenresBox.Items,
            AddTags = AddTagsBox.Items,
            RemoveTags = RemoveTagsBox.Items,
            Lock = LockBox.SelectedIndex switch { 1 => true, 2 => false, _ => null },
        };

        var nothing = change.Collection == null && change.AgeRating == null && change.Lock == null
                      && change.AddGenres.Count + change.RemoveGenres.Count + change.AddTags.Count + change.RemoveTags.Count == 0;
        if (nothing)
        {
            ErrorText.Text = "Tick or fill in at least one change.";
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true;
            return;
        }

        Result = BulkNfoEdit.Apply(_folders, change);
    }
}
