using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using CineLibraryEssentials.Services;
using CineLibraryEssentials.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace CineLibraryEssentials.Views;

/// <summary>
/// "Edit NFO": a resizable editor for a movie's or show's NFO, its artwork,
/// cast and (for TV) episodes. Only what the user changes is written (see
/// <see cref="NfoEditor"/>); every save keeps the previous files for
/// "Restore previous version" (see <see cref="NfoBackup"/>).
/// </summary>
public sealed partial class NfoEditorWindow : Window
{
    // One editor per folder: opening it again brings the open one to the front.
    private static readonly Dictionary<string, NfoEditorWindow> OpenEditors = new(StringComparer.OrdinalIgnoreCase);

    private static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"), ("my", "Burmese"), ("hi", "Hindi"), ("ta", "Tamil"), ("te", "Telugu"),
        ("th", "Thai"), ("zh", "Chinese"), ("ja", "Japanese"), ("ko", "Korean"), ("fr", "French"),
        ("de", "German"), ("es", "Spanish"), ("it", "Italian"), ("pt", "Portuguese"), ("ru", "Russian"),
        ("ar", "Arabic"),
    };

    private static readonly string[] AgeRatings =
        { "G", "PG", "PG-13", "R", "NC-17", "NR", "TV-Y", "TV-Y7", "TV-G", "TV-PG", "TV-14", "TV-MA" };

    private static readonly string[] Editions =
    {
        "Director's Cut", "Extended", "Extended Edition", "Theatrical", "Unrated", "IMAX", "Remastered",
        "4K Remaster", "Criterion", "Special Edition", "Ultimate Edition", "Final Cut", "Open Matte",
    };

    private readonly string _folder;
    private readonly List<string> _libraryFolders;
    private readonly ConfigService _config = new();
    private readonly ObservableCollection<ActorItem> _cast = new();
    private readonly List<ArtSlot> _slots = new();
    private readonly List<EpisodeItem> _episodes = new();
    private readonly Dictionary<string, (string Title, string Tagline, string Plot)> _fetchCache = new();

    private string _nfoPath = string.Empty;
    private XDocument _doc = new();
    private NfoEditor.Fields _before = new();
    private bool _beforeLocked;
    private bool _isTv;
    private string _ageRating = string.Empty;
    private NfoReaderService.MovieDetail? _detail;
    private LibraryVocabulary _vocab = LibraryVocabulary.Empty;
    private ImdbRatingService.ImdbRating? _pendingImdb;
    private ArtSlot? _gallerySlot;
    private (int Id, List<TmdbApiClient.ImageOption> Posters, List<TmdbApiClient.ImageOption> Backdrops, List<TmdbApiClient.ImageOption> Logos)? _images;
    private bool _loading;
    private bool _closeConfirmed;
    private bool _lockTouched;   // the user clicked "Protect" themselves

    /// <summary>True once anything was saved or restored while the window was open.</summary>
    public bool Saved { get; private set; }

    /// <summary>
    /// Raised with the folder after a save or restore, so Step 3's card and an
    /// open details window can show the new title and pictures right away.
    /// </summary>
    public static event Action<string>? FolderSaved;

    /// <summary>
    /// Opens the editor for a folder, or brings its open editor to the front.
    /// Returns an error message instead when the folder has no readable NFO.
    /// </summary>
    public static (NfoEditorWindow? Window, string? Error) Show(string folder, IEnumerable<string>? libraryFolders = null)
    {
        if (OpenEditors.TryGetValue(folder, out var open))
        {
            open.Activate();
            return (open, null);
        }
        if (NfoLocator.FindMainNfo(folder) == null)
            return (null, "There's no NFO in this folder yet. Scrape it first.");

        NfoEditorWindow window;
        try
        {
            window = new NfoEditorWindow(folder, libraryFolders ?? new[] { folder });
        }
        catch (Exception ex)
        {
            return (null, $"The NFO couldn't be read: {ex.Message}");
        }
        OpenEditors[folder] = window;
        window.Closed += (_, _) => OpenEditors.Remove(folder);
        window.CenterOn(App.MainWindow);
        window.Activate();
        return (window, null);
    }

    private NfoEditorWindow(string folder, IEnumerable<string> libraryFolders)
    {
        InitializeComponent();
        _folder = folder;
        _libraryFolders = libraryFolders.ToList();

        AppWindow.Resize(new SizeInt32(1240, 880));
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetIcon failed for editor window: {ex.Message}");
        }
        AppWindow.Closing += OnAppWindowClosing;

        foreach (var r in AgeRatings) AgeRatingBox.Items.Add(r);
        foreach (var b in new[] { FetchTitleButton, FetchTaglineButton, FetchPlotButton, FetchOutlineButton })
            SetupFetchFlyout(b);
        foreach (var box in new[] { GenresBox, TagsBox, StudiosBox, CountriesBox, DirectorsBox, WritersBox })
            box.Changed += (_, _) => UpdateDirty();

        CastList.ItemsSource = _cast;
        _cast.CollectionChanged += (_, _) => UpdateDirty();

        Load();
        _ = LoadSuggestionsAsync();
    }

    private void CenterOn(Window? owner)
    {
        if (owner == null) return;
        try
        {
            var pos = owner.AppWindow.Position;
            var size = owner.AppWindow.Size;
            var mine = AppWindow.Size;
            AppWindow.Move(new PointInt32(pos.X + (size.Width - mine.Width) / 2, pos.Y + (size.Height - mine.Height) / 2));
        }
        catch { /* default position is fine */ }
    }

    // =====================================================================
    //  Load
    // =====================================================================

    /// <summary>Reads everything from disk again (also after Save, Revert and Restore).</summary>
    private void Load()
    {
        _loading = true;
        try
        {
            _nfoPath = NfoLocator.FindMainNfo(_folder) ?? throw new FileNotFoundException("The NFO is gone.");
            _doc = NfoFile.Load(_nfoPath);
            var root = _doc.Root ?? throw new System.Xml.XmlException("The NFO is empty.");
            _isTv = root.Name.LocalName == "tvshow";
            _before = NfoEditor.Read(root);
            _beforeLocked = NfoEditor.IsLocked(root);
            _detail = new NfoReaderService().ReadFromFolder(_folder);
            _pendingImdb = null;

            HeaderTitle.Text = _before.Year > 0 ? $"{_before.Title} ({_before.Year})" : _before.Title;
            KindText.Text = _isTv ? "TV show" : "Movie";
            HeaderPoster.Source = _detail?.PosterPath is { } poster ? Fresh(poster, 96) : null;
            // Ticked by default: saving an edit protects it, unless the user unticks it
            LockCheckBox.IsChecked = true;
            _lockTouched = false;
            ProtectedBadge.Visibility = _beforeLocked ? Visibility.Visible : Visibility.Collapsed;

            // Details
            TitleBox.Text = _before.Title;
            OriginalTitleBox.Text = _before.OriginalTitle;
            SortTitleBox.Text = _before.SortTitle;
            YearBox.Value = _before.Year > 0 ? _before.Year : double.NaN;
            PremieredBox.Text = _before.Premiered;
            RuntimeBox.Value = _before.Runtime > 0 ? _before.Runtime : double.NaN;
            SetAgeRating(_before.AgeRating);
            TaglineBox.Text = _before.Tagline;
            PlotBox.Text = _before.Plot;
            OutlineBox.Text = _before.Outline;
            CollectionBox.Text = _before.Collection;
            EditionBox.Text = _before.Edition;

            // Lists
            GenresBox.Items = _before.Genres;
            TagsBox.Items = _before.Tags;
            StudiosBox.Items = _before.Studios;
            CountriesBox.Items = _before.Countries;
            DirectorsBox.Items = _before.Directors;
            WritersBox.Items = _before.Writers;

            // Ratings and IDs
            UserRatingControl.Value = _before.UserRating > 0 ? _before.UserRating : -1;
            UpdateUserRatingText();
            ImdbIdBox.Text = _before.ImdbId;
            TmdbIdBox.Text = _before.TmdbId;
            TrailerBox.Text = _before.Trailer;
            BuildRatingChips(RatingsXml.ReadAll(root));

            // Shows have creators instead of a director, and no collection or edition
            MovieOnlyDetails.Visibility = _isTv ? Visibility.Collapsed : Visibility.Visible;
            DirectorsBox.Visibility = _isTv ? Visibility.Collapsed : Visibility.Visible;
            WritersBox.Header = _isTv ? "Creators" : "Writers";
            StudiosBox.Header = _isTv ? "Studios and networks" : "Studios";
            EpisodesNavItem.Visibility = _isTv ? Visibility.Visible : Visibility.Collapsed;

            _cast.Clear();
            foreach (var a in _before.Cast)
                _cast.Add(NewActor(a.Name, a.Role, a.Thumb));

            BuildArtSlots();
            GalleryPanel.Visibility = Visibility.Collapsed;
            LoadEpisodes();

            SourcePathText.Text = _nfoPath;
            SourceText.Text = File.ReadAllText(_nfoPath);
            CopySourceText.Text = "Copy";
            UpdateRestoreButton();
        }
        finally
        {
            _loading = false;
        }
        UpdateDirty();
    }

    private async Task LoadSuggestionsAsync()
    {
        var folders = _libraryFolders;
        _vocab = await Task.Run(() => LibraryVocabulary.Collect(folders));
        GenresBox.Suggestions = _vocab.Genres;
        TagsBox.Suggestions = _vocab.Tags;
        StudiosBox.Suggestions = _vocab.Studios;
        CountriesBox.Suggestions = _vocab.Countries;
        DirectorsBox.Suggestions = _vocab.People;
        WritersBox.Suggestions = _vocab.People;
    }

    private void SetAgeRating(string value)
    {
        _ageRating = value;
        if (value.Length > 0 && !AgeRatingBox.Items.Contains(value)) AgeRatingBox.Items.Add(value);
        AgeRatingBox.SelectedItem = value.Length > 0 ? value : null;
        if (value.Length == 0) AgeRatingBox.Text = string.Empty;
    }

    // =====================================================================
    //  Changes
    // =====================================================================

    /// <summary>The fields as they are on screen now.</summary>
    private NfoEditor.Fields OnScreen() => _before with
    {
        Title = TitleBox.Text.Trim(),
        OriginalTitle = OriginalTitleBox.Text.Trim(),
        SortTitle = SortTitleBox.Text.Trim(),
        Year = ToInt(YearBox.Value),
        Premiered = PremieredBox.Text.Trim(),
        Runtime = ToInt(RuntimeBox.Value),
        Tagline = Norm(TaglineBox.Text),
        Plot = Norm(PlotBox.Text),
        Outline = Norm(OutlineBox.Text),
        Genres = Same(GenresBox.Items, _before.Genres),
        Tags = Same(TagsBox.Items, _before.Tags),
        Studios = Same(StudiosBox.Items, _before.Studios),
        Countries = Same(CountriesBox.Items, _before.Countries),
        Directors = _isTv ? _before.Directors : Same(DirectorsBox.Items, _before.Directors),
        Writers = Same(WritersBox.Items, _before.Writers),
        Cast = _cast.Select(a => a.ToActor()).ToList(),
        AgeRating = _ageRating.Trim(),
        UserRating = UserRatingControl.Value > 0 ? (int)Math.Round(UserRatingControl.Value) : 0,
        Collection = _isTv ? _before.Collection : CollectionBox.Text.Trim(),
        Edition = _isTv ? _before.Edition : EditionBox.Text.Trim(),
        Trailer = TrailerBox.Text.Trim(),
        ImdbId = ImdbIdBox.Text.Trim(),
        TmdbId = TmdbIdBox.Text.Trim(),
    };

    private int DirtyCount()
    {
        var n = NfoEditor.CountChanges(_before, OnScreen());
        // Only a lock change the user made counts on its own; the default tick rides along with other edits
        if (_lockTouched && (LockCheckBox.IsChecked == true) != _beforeLocked) n++;
        n += _slots.Count(s => s.HasPending);
        n += _cast.Count(a => a.PendingPhoto != null);
        n += _episodes.Count(e => e.IsChanged);
        if (_pendingImdb != null) n++;
        return n;
    }

    private void UpdateDirty()
    {
        if (_loading) return;
        var n = DirtyCount();
        UnsavedBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnsavedText.Text = n == 1 ? "1 unsaved change" : $"{n} unsaved changes";
        Title = (n > 0 ? "● " : string.Empty) + $"Edit NFO - {HeaderTitle.Text}";
    }

    private void OnAnyChange(object sender, object e) => UpdateDirty();

    private void OnLockClick(object sender, RoutedEventArgs e)
    {
        _lockTouched = true;
        UpdateDirty();
    }
    private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => UpdateDirty();

    private void OnAgeRatingSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AgeRatingBox.SelectedItem is string s) _ageRating = s;
        UpdateDirty();
    }

    private void OnAgeRatingSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        _ageRating = args.Text.Trim();
        UpdateDirty();
    }

    private void OnSuggestTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var pool = sender == CollectionBox ? _vocab.Collections : Editions;
            sender.ItemsSource = LibraryVocabulary.Match(pool, sender.Text);
        }
        UpdateDirty();
    }

    private void OnUserRatingChanged(RatingControl sender, object args)
    {
        UpdateUserRatingText();
        UpdateDirty();
    }

    private void UpdateUserRatingText()
        => UserRatingText.Text = UserRatingControl.Value > 0 ? $"{Math.Round(UserRatingControl.Value)} / 10" : "Not rated";

    // =====================================================================
    //  Save, Revert, Restore, Close
    // =====================================================================

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync();

    private async void OnSaveAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await SaveAsync();
    }

    private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (DirtyCount() == 0) Close();
        else _ = ConfirmCloseAsync();
    }

    private async Task<bool> SaveAsync()
    {
        var after = OnScreen();
        if (after.Title.Length == 0)
            return Fail("The title can't be empty.", "details");
        if (after.Year != 0 && (after.Year < 1870 || after.Year > DateTime.Now.Year + 5))
            return Fail($"The year should be between 1870 and {DateTime.Now.Year + 5}, or empty.", "details");
        if (!IsDateOrEmpty(after.Premiered))
            return Fail("Write the premiered date as year-month-day, like 2010-07-15.", "details");
        if (_episodes.FirstOrDefault(ep => ep.FieldsChanged && !IsDateOrEmpty(ep.Aired)) is { } badEpisode)
            return Fail($"Write the air date of {badEpisode.Code} as year-month-day, like 2017-12-01.", "episodes");

        if (DirtyCount() == 0)
        {
            ShowStatus("Nothing to save.", InfoBarSeverity.Informational);
            return true;
        }

        SaveButton.IsEnabled = false;
        try
        {
            // Everything this save touches goes into one backup first
            var backup = NfoBackup.Begin(_nfoPath);
            backup.Add(_nfoPath);

            foreach (var slot in _slots.Where(s => s.HasPending))
            {
                var target = ArtTarget(slot);
                backup.Add(target);
                if (slot.PendingFile != null) await ImageFiles.SaveAsAsync(slot.PendingFile, target);
                else await ImageFiles.DownloadAsync(ImageFiles.Tmdb(slot.PendingTmdb!.FilePath, "original"), target);
            }

            foreach (var actor in _cast.Where(a => a.PendingPhoto != null))
            {
                var file = ImageDownloadService.BuildActorFileName(actor.Name.Trim());
                var target = Path.Combine(_folder, ".actors", file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                backup.Add(target);
                await ImageFiles.SaveAsAsync(actor.PendingPhoto!, target);
                actor.Thumb = $".actors/{file}";
            }
            after = OnScreen();   // picks up the new actor photo paths

            var root = _doc.Root!;
            if (_pendingImdb is { } imdb)
                RatingsXml.ApplyImdb(root, imdb.Rating, imdb.Votes, _config.GetPreferImdbRating());
            NfoEditor.Apply(root, _before, after, LockCheckBox.IsChecked == true);
            NfoFile.Save(_doc, _nfoPath);

            foreach (var ep in _episodes.Where(x => x.IsChanged))
            {
                if (ep.FieldsChanged && ep.NfoPath != null)
                {
                    backup.Add(ep.NfoPath);
                    var epDoc = NfoFile.Load(ep.NfoPath);
                    NfoEditor.ApplyEpisode(epDoc.Root!, ep.Before, ep.Fields);
                    NfoFile.Save(epDoc, ep.NfoPath);
                }
                if (ep.PendingThumb != null)
                {
                    backup.Add(ep.ThumbTarget);
                    await ImageFiles.SaveAsAsync(ep.PendingThumb, ep.ThumbTarget);
                }
            }

            Saved = true;
            Load();
            ShowStatus("Saved. Restore previous version puts back what was there before.", InfoBarSeverity.Success);
            FolderSaved?.Invoke(_folder);
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't save: {ex.Message}", InfoBarSeverity.Error);
            return false;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private bool Fail(string message, string section)
    {
        SelectSection(section);
        ShowStatus(message, InfoBarSeverity.Error);
        return false;
    }

    private async void OnRevertClick(object sender, RoutedEventArgs e)
    {
        if (DirtyCount() == 0)
        {
            ShowStatus("There are no unsaved changes.", InfoBarSeverity.Informational);
            return;
        }
        if (await AskAsync("Throw away your changes?", "Everything you changed since the last save goes back to what's in the files.", "Revert") != ContentDialogResult.Primary)
            return;
        Load();
        ShowStatus("Changes thrown away.", InfoBarSeverity.Informational);
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        var when = NfoBackup.BackupTime(_nfoPath);
        if (when == null)
        {
            ShowStatus("There's no earlier version yet. One is kept each time you save.", InfoBarSeverity.Informational);
            return;
        }
        if (await AskAsync($"Restore the version from {when:g}?",
                "This puts back the NFO and any pictures your last save replaced. Unsaved changes are lost.",
                "Restore") != ContentDialogResult.Primary)
            return;
        try
        {
            var n = NfoBackup.Restore(_nfoPath);
            Saved = true;
            Load();
            ShowStatus($"Restored {n} file{(n == 1 ? "" : "s")} from {when:g}.", InfoBarSeverity.Success);
            FolderSaved?.Invoke(_folder);
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't restore: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void UpdateRestoreButton()
    {
        var when = NfoBackup.BackupTime(_nfoPath);
        ToolTipService.SetToolTip(RestoreButton, when == null
            ? "No earlier version yet. One is kept each time you save."
            : $"Put back the files as they were before your save on {when:g}");
    }

    // The title bar's close button and Alt+F4 (a programmatic Close() skips this)
    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || DirtyCount() == 0) return;
        args.Cancel = true;
        _ = ConfirmCloseAsync();
    }

    /// <summary>Asks Save / Don't save / Cancel, then closes unless cancelled.</summary>
    private async Task ConfirmCloseAsync()
    {
        var n = DirtyCount();
        ContentDialogResult answer;
        try
        {
            answer = await new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Save your changes?",
                Content = $"{(n == 1 ? "1 change" : $"{n} changes")} to {HeaderTitle.Text} haven't been saved.",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Don't save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            }.ShowAsync();
        }
        catch
        {
            return;   // another dialog is open; stay open
        }

        if (answer == ContentDialogResult.Primary && !await SaveAsync()) return;
        if (answer == ContentDialogResult.None) return;
        _closeConfirmed = true;
        Close();
    }

    private async Task<ContentDialogResult> AskAsync(string title, string message, string action)
    {
        try
        {
            return await new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = title,
                Content = message,
                PrimaryButtonText = action,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            }.ShowAsync();
        }
        catch
        {
            return ContentDialogResult.None;
        }
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    // =====================================================================
    //  Sections
    // =====================================================================

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (DetailsPanel == null) return;   // still loading the XAML
        ShowSection((args.SelectedItem as NavigationViewItem)?.Tag as string ?? "details");
    }

    private void ShowSection(string tag)
    {
        DetailsPanel.Visibility = tag == "details" ? Visibility.Visible : Visibility.Collapsed;
        GenresPanel.Visibility = tag == "genres" ? Visibility.Visible : Visibility.Collapsed;
        CastPanel.Visibility = tag == "cast" ? Visibility.Visible : Visibility.Collapsed;
        RatingsPanel.Visibility = tag == "ratings" ? Visibility.Visible : Visibility.Collapsed;
        ArtworkPanel.Visibility = tag == "artwork" ? Visibility.Visible : Visibility.Collapsed;
        EpisodesPanel.Visibility = tag == "episodes" ? Visibility.Visible : Visibility.Collapsed;
        SourcePanel.Visibility = tag == "source" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "source" && File.Exists(_nfoPath)) SourceText.Text = File.ReadAllText(_nfoPath);
    }

    private void SelectSection(string tag)
    {
        var item = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
        if (item != null) Nav.SelectedItem = item;
    }

    // =====================================================================
    //  Fetch a field from TMDb
    // =====================================================================

    private void SetupFetchFlyout(Button button)
    {
        var mine = _config.GetScrapeLanguage();
        var flyout = new MenuFlyout();
        foreach (var (code, name) in Languages.OrderBy(l => l.Code == mine ? 0 : l.Code == "en" ? 1 : 2))
        {
            var item = new MenuFlyoutItem { Text = code == mine && code != "en" ? $"{name} (your scrape language)" : name };
            item.Click += async (_, _) => await FetchFieldAsync((string)button.Tag, code, name);
            flyout.Items.Add(item);
        }
        button.Flyout = flyout;
    }

    private async Task FetchFieldAsync(string field, string lang, string langName)
    {
        if (!TryTmdbId(out var id)) return;
        var label = field switch { "title" => "title", "tagline" => "tagline", "outline" => "outline", _ => "plot" };

        var key = $"{id}:{lang}";
        if (!_fetchCache.TryGetValue(key, out var text))
        {
            ShowStatus($"Getting the {langName} {label} from TMDb...", InfoBarSeverity.Informational);
            var client = new TmdbApiClient(_config.GetApiKey() ?? string.Empty, lang);
            if (_isTv)
            {
                var d = await client.GetTvDetailsAsync(id);
                if (d == null) { ShowStatus("Couldn't reach TMDb. Check your connection and the TMDb id.", InfoBarSeverity.Warning); return; }
                text = (d.Name, d.Tagline, d.Overview);
            }
            else
            {
                var d = await client.GetMovieDetailsAsync(id);
                if (d == null) { ShowStatus("Couldn't reach TMDb. Check your connection and the TMDb id.", InfoBarSeverity.Warning); return; }
                text = (d.Title, d.Tagline, d.Overview);
            }
            _fetchCache[key] = text;
        }

        var value = field switch { "title" => text.Title, "tagline" => text.Tagline, _ => text.Plot };
        if (string.IsNullOrWhiteSpace(value))
        {
            ShowStatus($"TMDb has no {langName} {label} for this title.", InfoBarSeverity.Warning);
            return;
        }
        switch (field)
        {
            case "title": TitleBox.Text = value.Trim(); break;
            case "tagline": TaglineBox.Text = value.Trim(); break;
            case "outline": OutlineBox.Text = value.Trim(); break;
            default: PlotBox.Text = value.Trim(); break;
        }
        ShowStatus($"Filled in the {label} from TMDb ({langName}). Save to keep it.", InfoBarSeverity.Success);
    }

    private bool TryTmdbId(out int id)
    {
        if (int.TryParse(TmdbIdBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0)
            return true;
        SelectSection("ratings");
        ShowStatus("This needs the TMDb id. Add it under Ratings and IDs.", InfoBarSeverity.Warning);
        return false;
    }

    // =====================================================================
    //  Cast
    // =====================================================================

    private ActorItem NewActor(string name, string role, string thumb)
    {
        var item = new ActorItem { Name = name, Role = role, Thumb = thumb, Photo = PhotoFor(thumb) };
        item.PropertyChanged += (_, _) => UpdateDirty();
        return item;
    }

    private ImageSource? PhotoFor(string thumb)
    {
        if (thumb.Length == 0) return null;
        if (thumb.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return new BitmapImage(new Uri(thumb)) { DecodePixelWidth = 80 };
        var path = Path.Combine(_folder, thumb.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? Fresh(path, 80) : null;
    }

    private void OnAddActorClick(object sender, RoutedEventArgs e)
    {
        var item = NewActor(string.Empty, string.Empty, string.Empty);
        _cast.Add(item);
        CastList.ScrollIntoView(item);
    }

    private async void OnActorPhotoClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ActorItem actor) return;
        if (actor.Name.Trim().Length == 0)
        {
            ShowStatus("Type the actor's name first: the photo file is named after it.", InfoBarSeverity.Warning);
            return;
        }
        if (await PickImageAsync() is { } path)
        {
            actor.PendingPhoto = path;
            actor.Photo = Fresh(path, 80);
        }
    }

    private void OnActorUpClick(object sender, RoutedEventArgs e) => MoveActor(sender, -1);
    private void OnActorDownClick(object sender, RoutedEventArgs e) => MoveActor(sender, +1);

    private void MoveActor(object sender, int delta)
    {
        if ((sender as FrameworkElement)?.Tag is not ActorItem actor) return;
        var i = _cast.IndexOf(actor);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _cast.Count) return;
        _cast.Move(i, j);
    }

    private void OnActorRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ActorItem actor) _cast.Remove(actor);
    }

    // =====================================================================
    //  Ratings and IDs
    // =====================================================================

    private void BuildRatingChips(IEnumerable<RatingsXml.Entry> entries)
    {
        RatingsChips.Children.Clear();
        foreach (var r in entries)
        {
            RatingsChips.Children.Add(new Border
            {
                Background = Brush("AccentFillColorTertiaryBrush"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 4, 12, 4),
                Child = new TextBlock
                {
                    Text = NfoReaderService.MovieDetail.RatingLabel(r) + (r.IsDefault ? "  (main)" : string.Empty),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                },
            });
        }
        if (RatingsChips.Children.Count == 0)
            RatingsChips.Children.Add(new TextBlock { Text = "No ratings in this NFO yet.", Foreground = Brush("TextFillColorSecondaryBrush") });
    }

    private async void OnRefreshImdbClick(object sender, RoutedEventArgs e)
    {
        var id = ImdbIdBox.Text.Trim();
        if (!ImdbRatingService.IsImdbId(id))
        {
            ShowStatus("This needs an IMDb id like tt1375666.", InfoBarSeverity.Warning);
            return;
        }
        RefreshImdbButton.IsEnabled = false;
        try
        {
            var rating = await new ImdbRatingService().GetRatingAsync(id);
            if (rating == null)
            {
                ShowStatus("Couldn't get an IMDb rating right now. Try again later.", InfoBarSeverity.Warning);
                return;
            }
            _pendingImdb = rating;
            var preview = new XElement(_doc.Root!);
            RatingsXml.ApplyImdb(preview, rating.Rating, rating.Votes, _config.GetPreferImdbRating());
            BuildRatingChips(RatingsXml.ReadAll(preview));
            ShowStatus("Got the latest IMDb rating. Save to keep it.", InfoBarSeverity.Success);
            UpdateDirty();
        }
        finally
        {
            RefreshImdbButton.IsEnabled = true;
        }
    }

    private void OnOpenImdbClick(object sender, RoutedEventArgs e)
    {
        var id = ImdbIdBox.Text.Trim();
        if (ImdbRatingService.IsImdbId(id)) OpenUrl($"https://www.imdb.com/title/{id}/");
    }

    private void OnOpenTmdbClick(object sender, RoutedEventArgs e)
    {
        if (TryTmdbId(out var id)) OpenUrl($"https://www.themoviedb.org/{(_isTv ? "tv" : "movie")}/{id}");
    }

    private void OnOpenTrailerClick(object sender, RoutedEventArgs e)
    {
        var url = TrailerBox.Text.Trim();
        // Kodi's YouTube add-on form: plugin://plugin.video.youtube/?action=play_video&videoid=ID
        var m = System.Text.RegularExpressions.Regex.Match(url, @"videoid=([\w-]+)");
        if (m.Success) url = $"https://www.youtube.com/watch?v={m.Groups[1].Value}";
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) OpenUrl(url);
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"Open link failed: {ex.Message}"); }
    }

    // =====================================================================
    //  Artwork
    // =====================================================================

    private sealed class ArtSlot
    {
        public required string Key { get; init; }
        public required string Label { get; init; }
        public required string Ext { get; init; }
        public string? Source { get; init; }   // which TMDb list, or null when TMDb has none (banner)
        public string? Current { get; init; }
        public string? PendingFile { get; set; }
        public TmdbApiClient.ImageOption? PendingTmdb { get; set; }
        public Image Preview { get; } = new() { Stretch = Stretch.Uniform };
        public TextBlock Status { get; } = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        public bool HasPending => PendingFile != null || PendingTmdb != null;
    }

    private void BuildArtSlots()
    {
        _slots.Clear();
        ArtSlotsPanel.Children.Clear();
        AddSlot("poster", "Poster", ".jpg", "posters", 150, 225, _detail?.PosterPath);
        AddSlot("fanart", "Fanart", ".jpg", "backdrops", 320, 180, _detail?.FanartPath);
        AddSlot("clearlogo", "Clear logo", ".png", "logos", 320, 124, FindArt("clearlogo", ".png"));
        AddSlot("landscape", "Landscape", ".jpg", "backdrops", 320, 180, FindArt("landscape", ".jpg"));
        AddSlot("banner", "Banner", ".jpg", null, 380, 70, FindArt("banner", ".jpg"));
    }

    private void AddSlot(string key, string label, string ext, string? source, double width, double height, string? current)
    {
        var slot = new ArtSlot { Key = key, Label = label, Ext = ext, Source = source, Current = current };
        if (current != null) slot.Preview.Source = Fresh(current, (int)width * 2);
        slot.Status.Text = current != null ? Path.GetFileName(current) : "None yet";
        slot.Status.Foreground = Brush("TextFillColorSecondaryBrush");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (source != null)
        {
            var fromTmdb = new Button { Content = "From TMDb" };
            fromTmdb.Click += async (_, _) => await OpenGalleryAsync(slot);
            buttons.Children.Add(fromTmdb);
        }
        var fromFile = new Button { Content = "From file..." };
        fromFile.Click += async (_, _) =>
        {
            if (await PickImageAsync() is not { } path) return;
            slot.PendingFile = path;
            slot.PendingTmdb = null;
            slot.Preview.Source = Fresh(path, (int)width * 2);
            slot.Status.Text = $"{Path.GetFileName(path)}, written when you save";
            UpdateDirty();
        };
        buttons.Children.Add(fromFile);

        var card = new StackPanel { Spacing = 8, Width = width };
        card.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        card.Children.Add(new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(4),
            Background = Brush("ControlFillColorTertiaryBrush"),
            Child = slot.Preview,
        });
        card.Children.Add(slot.Status);
        card.Children.Add(buttons);

        ArtSlotsPanel.Children.Add(new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("CardStrokeColorDefaultBrush"),
            Background = Brush("CardBackgroundFillColorDefaultBrush"),
            Child = card,
        });
        _slots.Add(slot);
    }

    /// <summary>An existing extra-art file: "Movie-clearlogo.png", or "clearlogo.png" in the folder.</summary>
    private string? FindArt(string key, string ext)
    {
        var candidates = new List<string> { Path.Combine(_folder, key + ext) };
        if (!_isTv)
        {
            var basis = _detail?.VideoFilePath ?? _nfoPath;
            candidates.Insert(0, Path.Combine(Path.GetDirectoryName(basis)!, $"{Path.GetFileNameWithoutExtension(basis)}-{key}{ext}"));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Where a new picture goes: over the existing one, else the name Kodi expects.</summary>
    private string ArtTarget(ArtSlot slot)
    {
        if (_isTv) return Path.Combine(_folder, slot.Key + slot.Ext);
        if (slot.Current != null && slot.Current.EndsWith(slot.Ext, StringComparison.OrdinalIgnoreCase))
            return slot.Current;
        var basis = _detail?.VideoFilePath ?? _nfoPath;
        return Path.Combine(Path.GetDirectoryName(basis)!, $"{Path.GetFileNameWithoutExtension(basis)}-{slot.Key}{slot.Ext}");
    }

    private async Task OpenGalleryAsync(ArtSlot slot)
    {
        if (!TryTmdbId(out var id)) return;
        _gallerySlot = slot;
        GalleryTitle.Text = $"{slot.Label} pictures on TMDb";
        GalleryPanel.Visibility = Visibility.Visible;

        if (_images?.Id != id)
        {
            GalleryGrid.ItemsSource = null;
            GalleryEmptyText.Text = "Getting the pictures from TMDb...";
            GalleryEmptyText.Visibility = Visibility.Visible;
            var (posters, backdrops, logos) = await new TmdbApiClient(_config.GetApiKey() ?? string.Empty).GetImagesAsync(id, _isTv);
            _images = (id, posters, backdrops, logos);
        }

        if (GalleryLanguageBox.Items.Count == 0)
        {
            var mine = _config.GetScrapeLanguage();
            GalleryLanguageBox.Items.Add(new ComboBoxItem { Content = "All languages", Tag = "all" });
            GalleryLanguageBox.Items.Add(new ComboBoxItem { Content = "Without text", Tag = "none" });
            GalleryLanguageBox.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
            if (mine != "en")
            {
                var name = Languages.FirstOrDefault(l => l.Code == mine).Name ?? mine;
                GalleryLanguageBox.Items.Add(new ComboBoxItem { Content = name, Tag = mine });
            }
            GalleryLanguageBox.SelectedIndex = 0;
        }
        FillGallery();
        GalleryPanel.StartBringIntoView();
    }

    private void FillGallery()
    {
        if (_gallerySlot == null || _images == null) return;
        var (_, posters, backdrops, logos) = _images.Value;
        var all = _gallerySlot.Source switch { "posters" => posters, "logos" => logos, _ => backdrops };
        var filter = (GalleryLanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        var picked = all.Where(o => filter == "all" || (filter == "none" ? o.Language == null : o.Language == filter));

        var (w, h, size) = _gallerySlot.Source switch
        {
            "posters" => (150.0, 225.0, "w342"),
            "logos" => (260.0, 100.0, "w300"),
            _ => (260.0, 146.0, "w300"),
        };
        var items = picked.Select(o => new GalleryItem
        {
            Option = o,
            PreviewUrl = ImageFiles.Tmdb(o.FilePath, size),
            ItemWidth = w,
            ImageHeight = h,
            Caption = $"{o.Width}×{o.Height}" + (o.Language == null ? " · no text" : $" · {o.Language}"),
        }).ToList();

        GalleryGrid.ItemsSource = items;
        GalleryEmptyText.Text = all.Count == 0
            ? $"TMDb has no {_gallerySlot.Label.ToLowerInvariant()} pictures for this title."
            : $"No {_gallerySlot.Label.ToLowerInvariant()} pictures in this language. Try All languages.";
        GalleryEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnGalleryLanguageChanged(object sender, SelectionChangedEventArgs e) => FillGallery();

    private void OnGalleryCloseClick(object sender, RoutedEventArgs e) => GalleryPanel.Visibility = Visibility.Collapsed;

    private void OnGalleryItemClick(object sender, ItemClickEventArgs e)
    {
        if (_gallerySlot == null || e.ClickedItem is not GalleryItem item) return;
        _gallerySlot.PendingTmdb = item.Option;
        _gallerySlot.PendingFile = null;
        _gallerySlot.Preview.Source = new BitmapImage(new Uri(ImageFiles.Tmdb(item.Option.FilePath, "w500")));
        _gallerySlot.Status.Text = "From TMDb, written when you save";
        GalleryPanel.Visibility = Visibility.Collapsed;
        UpdateDirty();
    }

    // =====================================================================
    //  Episodes (TV)
    // =====================================================================

    private void LoadEpisodes()
    {
        _episodes.Clear();
        if (_isTv)
        {
            foreach (var e in NfoReaderService.ReadEpisodes(_folder))
            {
                var nfo = Path.ChangeExtension(e.VideoFile, ".nfo");
                string? nfoPath = null;
                var before = new NfoEditor.EpisodeFields(string.Empty, string.Empty, string.Empty);
                if (File.Exists(nfo))
                {
                    try
                    {
                        before = NfoEditor.ReadEpisode(XDocument.Load(nfo).Root!);
                        nfoPath = nfo;
                    }
                    catch { /* unreadable: shown as "no NFO yet" */ }
                }
                var basePath = Path.Combine(Path.GetDirectoryName(e.VideoFile)!, Path.GetFileNameWithoutExtension(e.VideoFile));
                var item = new EpisodeItem
                {
                    Code = e.LastEpisode > e.Episode ? $"S{e.Season:D2}E{e.Episode:D2}-E{e.LastEpisode:D2}" : $"S{e.Season:D2}E{e.Episode:D2}",
                    NfoPath = nfoPath,
                    Before = before,
                    ThumbTarget = basePath + "-thumb.jpg",
                    Title = nfoPath != null ? before.Title : e.Title,
                    Aired = before.Aired,
                    Plot = before.Plot,
                    Thumb = e.ThumbPath != null ? Fresh(e.ThumbPath, 480) : null,
                };
                item.PropertyChanged += (_, a) =>
                {
                    if (a.PropertyName is nameof(EpisodeItem.Title) or nameof(EpisodeItem.Aired) or nameof(EpisodeItem.Plot))
                        UpdateDirty();
                };
                _episodes.Add(item);
            }
        }
        EpisodeList.ItemsSource = _episodes.ToList();
        EpisodeList.SelectedIndex = _episodes.Count > 0 ? 0 : -1;
        if (_episodes.Count == 0) EpisodeEditor.Visibility = Visibility.Collapsed;
    }

    private void OnEpisodeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (EpisodeList.SelectedItem is not EpisodeItem item) return;
        EpisodeEditor.Visibility = Visibility.Visible;
        EpisodeEditor.DataContext = item;
        EpisodeHeading.Text = item.Code;
        var hasNfo = item.NfoPath != null;
        EpisodeFieldsPanel.Visibility = hasNfo ? Visibility.Visible : Visibility.Collapsed;
        EpisodeNoNfoText.Visibility = hasNfo ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnEpisodeThumbClick(object sender, RoutedEventArgs e)
    {
        if (EpisodeList.SelectedItem is not EpisodeItem item) return;
        if (await PickImageAsync() is not { } path) return;
        item.PendingThumb = path;
        item.Thumb = Fresh(path, 480);
        UpdateDirty();
    }

    // =====================================================================
    //  NFO source
    // =====================================================================

    private void OnCopySourceClick(object sender, RoutedEventArgs e)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(SourceText.Text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        CopySourceText.Text = "Copied";
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => OpenUrl(_folder);

    // =====================================================================
    //  Helpers
    // =====================================================================

    private async Task<string?> PickImageAsync()
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" })
            picker.FileTypeFilter.Add(ext);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <summary>A local picture that ignores the image cache, so a replaced file shows its new content.</summary>
    private static BitmapImage? Fresh(string path, int decodeWidth)
    {
        try
        {
            return new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                DecodePixelWidth = decodeWidth,
                UriSource = new Uri(path),
            };
        }
        catch
        {
            return null;
        }
    }

    private static Brush? Brush(string key)
    {
        try { return Application.Current.Resources[key] as Brush; }
        catch { return null; }
    }

    private static int ToInt(double value) => double.IsNaN(value) ? 0 : (int)Math.Round(value);

    /// <summary>The text box gives "\r" line breaks; the NFO has "\n".</summary>
    internal static string Norm(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    /// <summary>The original list when the values didn't change, so an untouched field compares equal.</summary>
    private static IReadOnlyList<string> Same(IReadOnlyList<string> now, IReadOnlyList<string> before)
        => now.SequenceEqual(before) ? before : now;

    private static bool IsDateOrEmpty(string text)
        => text.Trim().Length == 0
           || DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}

/// <summary>A cast row in the editor.</summary>
public partial class ActorItem : ObservableObject
{
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string role = string.Empty;
    [ObservableProperty] private ImageSource? photo;

    /// <summary>The NFO's &lt;thumb&gt; text.</summary>
    public string Thumb { get; set; } = string.Empty;

    /// <summary>A picked photo, copied into .actors/ on Save.</summary>
    public string? PendingPhoto { get; set; }

    public NfoEditor.Actor ToActor() => new(Name.Trim(), Role.Trim(), Thumb);
}

/// <summary>An episode row in the editor's Episodes section.</summary>
public partial class EpisodeItem : ObservableObject
{
    public string Code { get; init; } = string.Empty;
    public string? NfoPath { get; init; }
    public NfoEditor.EpisodeFields Before { get; init; } = new(string.Empty, string.Empty, string.Empty);
    public string ThumbTarget { get; init; } = string.Empty;
    public string? PendingThumb { get; set; }

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string aired = string.Empty;
    [ObservableProperty] private string plot = string.Empty;
    [ObservableProperty] private ImageSource? thumb;

    public NfoEditor.EpisodeFields Fields => new(Title.Trim(), Aired.Trim(), NfoEditorWindow.Norm(Plot));
    public bool FieldsChanged => NfoPath != null && Fields != Before;
    public bool IsChanged => FieldsChanged || PendingThumb != null;
}

/// <summary>A picture in the "from TMDb" gallery.</summary>
public class GalleryItem
{
    public TmdbApiClient.ImageOption Option { get; init; } = new(string.Empty, 0, 0, null, 0);
    public string PreviewUrl { get; init; } = string.Empty;
    public double ItemWidth { get; init; }
    public double ImageHeight { get; init; }
    public string Caption { get; init; } = string.Empty;
}
