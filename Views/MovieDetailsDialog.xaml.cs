using System.Diagnostics;
using CineLibraryEssentials.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace CineLibraryEssentials.Views;

/// <summary>
/// Rich movie details — a standalone resizable Window (not a ContentDialog) so
/// the user can maximize, drag, and never have content clipped. Adapted from
/// the CineLibrary viewer's modal. Reads the .nfo + local images Step 3 wrote.
/// </summary>
public sealed partial class MovieDetailsDialog : Window
{
    private NfoReaderService.MovieDetail? _detail;
    private TaskCompletionSource<bool>? _closedTcs;
    private List<NfoReaderService.EpisodeEntry> _episodes = new();
    private bool _isClosed;   // the background missing-episodes check must not touch a closed window

    public MovieDetailsDialog()
    {
        InitializeComponent();
        Title = "Movie Details";

        // Open at a comfortable size; user can resize / maximize freely.
        AppWindow.Resize(new SizeInt32(1180, 820));

        // Set the same app icon the MainWindow uses so the title bar / taskbar
        // shows the proper CineLibrary Essentials icon instead of the default.
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
                AppWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetIcon failed for details window: {ex.Message}");
        }

        NfoEditorWindow.FolderSaved += OnFolderSaved;
        Closed += (_, _) =>
        {
            _isClosed = true;
            NfoEditorWindow.FolderSaved -= OnFolderSaved;
            _closedTcs?.TrySetResult(true);
        };
    }

    /// <summary>
    /// Reads the .nfo + local images from the given movie folder and populates
    /// the window. Returns true if the folder had readable metadata.
    /// </summary>
    public bool LoadFromFolder(string folderPath)
    {
        _detail = new NfoReaderService().ReadFromFolder(folderPath);
        if (_detail == null) return false;

        ResetView();   // also runs after Edit, so start from a clean slate
        Title = _detail.DisplayName;

        // ---- Hero & poster ----
        if (!string.IsNullOrEmpty(_detail.FanartPath))
            HeroImage.Source = LoadLocalImage(_detail.FanartPath);

        if (!string.IsNullOrEmpty(_detail.PosterPath))
            PosterImage.Source = LoadLocalImage(_detail.PosterPath);
        else
            PosterPlaceholder.Visibility = Visibility.Visible;

        // ---- Title / original / tagline ----
        TitleText.Text = _detail.Title;

        if (_detail.HasOriginalTitle)
        {
            OriginalTitleText.Text = _detail.OriginalTitle;
            OriginalTitleText.Visibility = Visibility.Visible;
        }
        if (!string.IsNullOrWhiteSpace(_detail.Tagline))
        {
            TaglineText.Text = $"“{_detail.Tagline}”";
            TaglineText.Visibility = Visibility.Visible;
        }

        // ---- Chips ----
        AddChip(_detail.Year > 0 ? _detail.Year.ToString() : null);
        AddChip(_detail.RuntimeDisplay);
        AddChip(_detail.Mpaa);
        // One chip per rating source (IMDb, TMDb, ...); older NFOs have a single rating.
        if (_detail.Ratings.Count > 0)
            foreach (var r in _detail.Ratings) AddChip(NfoReaderService.MovieDetail.RatingLabel(r), accent: true);
        else
            AddChip(_detail.RatingDisplay, accent: true);
        AddChip(_detail.Edition, accent: true);

        // ---- Plot ----
        if (!string.IsNullOrWhiteSpace(_detail.Plot))
        {
            PlotText.Text = _detail.Plot;
            PlotSection.Visibility = Visibility.Visible;
        }

        // ---- Details grid ----
        if (_detail.Directors.Count > 0)
        {
            DirectorText.Text = string.Join(", ", _detail.Directors);
            DirectorField.Visibility = Visibility.Visible;
        }
        if (_detail.Writers.Count > 0)
        {
            WriterText.Text = string.Join(", ", _detail.Writers);
            WriterField.Visibility = Visibility.Visible;
        }
        if (_detail.Studios.Count > 0)
        {
            StudioText.Text = string.Join(", ", _detail.Studios);
            StudioField.Visibility = Visibility.Visible;
        }
        if (_detail.Countries.Count > 0)
        {
            CountryText.Text = string.Join(", ", _detail.Countries);
            CountryField.Visibility = Visibility.Visible;
        }
        if (_detail.Genres.Count > 0)
        {
            GenresText.Text = string.Join(", ", _detail.Genres);
            GenresField.Visibility = Visibility.Visible;
        }

        // IDs block
        var idLines = new List<string>();
        if (!string.IsNullOrEmpty(_detail.ImdbId)) idLines.Add($"IMDb  {_detail.ImdbId}");
        if (!string.IsNullOrEmpty(_detail.TmdbId)) idLines.Add($"TMDb  {_detail.TmdbId}");
        if (idLines.Count > 0)
        {
            IdsText.Text = string.Join("\n", idLines);
            IdsField.Visibility = Visibility.Visible;
        }

        // Stream details strip
        var streamParts = new List<string>();
        if (_detail.VideoWidth > 0 && _detail.VideoHeight > 0)
            streamParts.Add(ResolutionLabel(_detail.VideoWidth, _detail.VideoHeight));
        if (!string.IsNullOrEmpty(_detail.VideoCodec)) streamParts.Add(_detail.VideoCodec);
        if (_detail.DurationSeconds > 0) streamParts.Add(FormatDuration(_detail.DurationSeconds));
        if (_detail.AudioLanguages.Count > 0)
            streamParts.Add(string.Join("·", _detail.AudioLanguages.Distinct()));
        if (_detail.SubtitleLanguages.Count > 0)
            streamParts.Add($"{_detail.SubtitleLanguages.Count} sub{(_detail.SubtitleLanguages.Count == 1 ? "" : "s")}");
        if (streamParts.Count > 0)
        {
            StreamText.Text = string.Join("  ·  ", streamParts);
            StreamField.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrEmpty(_detail.TrailerUrl))
            TrailerButton.Visibility = Visibility.Visible;

        PlayButton.IsEnabled = !string.IsNullOrEmpty(_detail.VideoFilePath);

        // ---- Cast ----
        if (_detail.Actors.Count > 0)
        {
            CastList.ItemsSource = _detail.Actors.Select(a => new CastVm
            {
                Name = a.Name,
                Role = a.Role,
                HasPhoto = !string.IsNullOrEmpty(a.PhotoPath),
                PhotoSource = string.IsNullOrEmpty(a.PhotoPath) ? null : LoadLocalImage(a.PhotoPath)
            }).ToList();
            CastSection.Visibility = Visibility.Visible;
        }

        // ---- Episodes (TV) ----
        if (_detail.IsTvShow)
        {
            _episodes = NfoReaderService.ReadEpisodes(folderPath);
            if (_episodes.Count > 0)
            {
                RenderEpisodes(null);
                EpisodesSection.Visibility = Visibility.Visible;
                _ = CheckMissingEpisodesAsync();
            }
        }

        return true;
    }

    /// <summary>Hides every optional section and clears the chips before (re)loading.</summary>
    private void ResetView()
    {
        ChipsRow.Children.Clear();
        SeasonsPanel.Children.Clear();
        MissingSummaryText.Text = string.Empty;
        HeroImage.Source = null;
        PosterImage.Source = null;
        foreach (var element in new UIElement[]
                 {
                     PosterPlaceholder, OriginalTitleText, TaglineText, PlotSection, DirectorField,
                     WriterField, StudioField, CountryField, GenresField, IdsField, StreamField,
                     TrailerButton, CastSection, EpisodesSection,
                 })
            element.Visibility = Visibility.Collapsed;
    }

    /// <summary>Every folder in Step 3's list, for the editor's suggestions while typing.</summary>
    public IReadOnlyList<string> LibraryFolders { get; set; } = Array.Empty<string>();

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_detail == null) return;
        var (_, error) = NfoEditorWindow.Show(_detail.FolderPath,
            LibraryFolders.Count > 0 ? LibraryFolders : new[] { _detail.FolderPath });
        if (error == null) return;

        await new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Can't edit this NFO",
            Content = error,
            CloseButtonText = "OK",
        }.ShowAsync();
    }

    /// <summary>The editor saved this folder: show the new details right away.</summary>
    private void OnFolderSaved(string folder)
    {
        if (!_isClosed && _detail != null
            && string.Equals(folder, _detail.FolderPath, StringComparison.OrdinalIgnoreCase))
            LoadFromFolder(folder);
    }

    /// <summary>
    /// Asks TMDb which aired episodes aren't on disk and adds them to the list.
    /// Skipped quietly without an API key or a TMDb id in tvshow.nfo.
    /// </summary>
    private async Task CheckMissingEpisodesAsync()
    {
        var config = new ConfigService();
        var apiKey = config.GetApiKey();
        if (string.IsNullOrEmpty(apiKey) || !int.TryParse(_detail?.TmdbId, out var tvId)) return;

        MissingSummaryText.Text = "Checking TMDb for missing episodes...";
        var local = new HashSet<(int Season, int Episode)>();
        foreach (var e in _episodes)
            for (int n = e.Episode; n <= e.LastEpisode; n++) local.Add((e.Season, n));

        List<MissingEpisodes.Gap>? gaps;
        try
        {
            gaps = await MissingEpisodes.CheckAsync(new TmdbApiClient(apiKey, config.GetScrapeLanguage()), tvId, local);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Missing-episodes check failed: {ex.Message}");
            gaps = null;
        }
        if (_isClosed) return;

        if (gaps == null)
        {
            MissingSummaryText.Text = "Couldn't reach TMDb to check for missing episodes.";
            return;
        }
        var total = MissingEpisodes.Total(gaps);
        MissingSummaryText.Text = total == 0
            ? "Every aired episode is here."
            : $"{total} aired episode{(total == 1 ? " is" : "s are")} not in your library.";
        if (gaps.Count > 0) RenderEpisodes(gaps);
    }

    /// <summary>One block per season (specials last): episodes on disk plus any missing ones.</summary>
    private void RenderEpisodes(List<MissingEpisodes.Gap>? gaps)
    {
        SeasonsPanel.Children.Clear();
        var seasons = _episodes.Select(e => e.Season)
            .Concat(gaps?.Select(g => g.Season) ?? Enumerable.Empty<int>())
            .Distinct()
            .OrderBy(s => s == 0 ? int.MaxValue : s);

        foreach (var season in seasons)
        {
            var onDisk = _episodes.Where(e => e.Season == season).ToList();
            var missing = gaps?.Where(g => g.Season == season).ToList() ?? new();
            var diskCount = onDisk.Sum(e => e.LastEpisode - e.Episode + 1);
            var missingCount = MissingEpisodes.Total(missing);

            var header = new List<string> { season == 0 ? "Specials" : $"Season {season}" };
            if (diskCount > 0) header.Add($"{diskCount} episode{(diskCount == 1 ? "" : "s")}");
            if (missingCount > 0) header.Add($"{missingCount} missing");

            var block = new StackPanel { Spacing = 6 };
            block.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", header),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });

            var rows = onDisk.Select(e => (e.Episode, Row: EpisodeRow(e)))
                .Concat(missing.Select(g => (g.Episode, Row: MissingRow(g))))
                .OrderBy(r => r.Episode);
            foreach (var r in rows) block.Children.Add(r.Row);
            SeasonsPanel.Children.Add(block);
        }
    }

    private FrameworkElement EpisodeRow(NfoReaderService.EpisodeEntry e)
    {
        var number = e.LastEpisode > e.Episode ? $"E{e.Episode:D2}-E{e.LastEpisode:D2}" : $"E{e.Episode:D2}";
        var row = BuildEpisodeRow(e.ThumbPath, number, e.Title, AiredText(e.Aired), missing: false);
        ToolTipService.SetToolTip(row, $"{Path.GetFileName(e.VideoFile)}\nDouble-click to play");
        row.DoubleTapped += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = e.VideoFile, UseShellExecute = true }); }
            catch (Exception ex) { Debug.WriteLine($"Play episode failed: {ex.Message}"); }
        };
        return row;
    }

    private FrameworkElement MissingRow(MissingEpisodes.Gap g) => g.Episode == 0
        // A whole season that isn't on disk
        ? BuildEpisodeRow(null, string.Empty,
            g.Count == 1 ? "1 aired episode" : $"All {g.Count} aired episodes",
            "None of this season is in your library", missing: true)
        : BuildEpisodeRow(null, $"E{g.Episode:D2}", g.Title, AiredText(g.AirDate), missing: true);

    private Grid BuildEpisodeRow(string? thumbPath, string number, string title, string subtitle, bool missing)
    {
        var grid = new Grid { ColumnSpacing = 12, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumb = new Border
        {
            Width = 112,
            Height = 63,
            CornerRadius = new CornerRadius(4),
            Background = (Brush)Application.Current.Resources["ControlFillColorTertiaryBrush"],
            Child = thumbPath != null
                ? new Image
                {
                    Source = new BitmapImage(new Uri(thumbPath)) { DecodePixelWidth = 224 },
                    Stretch = Stretch.UniformToFill,
                }
                : new FontIcon
                {
                    Glyph = missing ? "" : "",
                    FontSize = 18,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                },
        };
        grid.Children.Add(thumb);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(number) ? title : $"{number}  {title}",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrEmpty(subtitle))
        {
            text.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (missing)
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 202, 80, 16)),
                Child = new TextBlock
                {
                    Text = "Missing",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                },
            };
            Grid.SetColumn(pill, 2);
            grid.Children.Add(pill);
        }
        return grid;
    }

    private static string AiredText(string aired)
        => DateTime.TryParse(aired, System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.None, out var d)
            ? $"Aired {d:d MMM yyyy}"
            : string.Empty;

    /// <summary>
    /// Shows the window centered on the owner and resolves once the user closes it.
    /// </summary>
    public Task ShowDialogAsync(Window? owner = null)
    {
        _closedTcs = new TaskCompletionSource<bool>();

        if (owner is not null)
        {
            try
            {
                var ownerPos = owner.AppWindow.Position;
                var ownerSize = owner.AppWindow.Size;
                var mySize = AppWindow.Size;
                AppWindow.Move(new PointInt32(
                    ownerPos.X + (ownerSize.Width - mySize.Width) / 2,
                    ownerPos.Y + (ownerSize.Height - mySize.Height) / 2));
            }
            catch { /* if positioning fails, just open at default */ }
        }

        Activate();
        return _closedTcs.Task;
    }

    // --- Handlers ----------------------------------------------------

    private void OnEscPressed(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        this.Close();
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_detail?.VideoFilePath)) return;
        try { Process.Start(new ProcessStartInfo { FileName = _detail.VideoFilePath, UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"Play failed: {ex.Message}"); }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_detail?.FolderPath)) return;
        try { Process.Start(new ProcessStartInfo { FileName = _detail.FolderPath, UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"Open folder failed: {ex.Message}"); }
    }

    private void OnTrailerClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_detail?.TrailerUrl)) return;
        try { Process.Start(new ProcessStartInfo { FileName = _detail.TrailerUrl, UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"Open trailer failed: {ex.Message}"); }
    }

    // --- Helpers -----------------------------------------------------

    private void AddChip(string? text, bool accent = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var brush = accent
            ? (Brush)Application.Current.Resources["AccentFillColorTertiaryBrush"]
            : (Brush)Application.Current.Resources["ControlFillColorTertiaryBrush"];
        var border = new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 4, 12, 4),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            }
        };
        ChipsRow.Children.Add(border);
    }

    private static BitmapImage? LoadLocalImage(string path)
    {
        // IgnoreImageCache: after Edit swaps the poster, the same path must show the new picture
        try { return new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache, UriSource = new Uri(path) }; }
        catch { return null; }
    }

    private static string ResolutionLabel(int w, int h)
    {
        if (h >= 2160) return "4K";
        if (h >= 1440) return "1440p";
        if (h >= 1080) return "1080p";
        if (h >= 720)  return "720p";
        if (h >= 480)  return "480p";
        return $"{w}×{h}";
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds <= 0) return string.Empty;
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes:00}m";
        return $"{ts.Minutes}m";
    }

    private class CastVm
    {
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool HasPhoto { get; set; }
        public BitmapImage? PhotoSource { get; set; }
    }
}
