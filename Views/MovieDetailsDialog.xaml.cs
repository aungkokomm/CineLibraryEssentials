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

        Closed += (_, _) => _closedTcs?.TrySetResult(true);
    }

    /// <summary>
    /// Reads the .nfo + local images from the given movie folder and populates
    /// the window. Returns true if the folder had readable metadata.
    /// </summary>
    public bool LoadFromFolder(string folderPath)
    {
        _detail = new NfoReaderService().ReadFromFolder(folderPath);
        if (_detail == null) return false;

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

        return true;
    }

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
        try { return new BitmapImage(new Uri(path)); }
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
