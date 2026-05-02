using CommunityToolkit.Mvvm.ComponentModel;

namespace CineLibraryEssentials.Models;

public class ProcessingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
}

public class FileOperation
{
    public string OriginalFilePath { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string CleanedTitle { get; set; } = string.Empty;
    public int Year { get; set; }
    public double Confidence { get; set; }
    public string DestinationFolder { get; set; } = string.Empty;
    public string FinalFileName { get; set; } = string.Empty;

    /// <summary>Whether this operation will be executed when "Run File to Folder" is clicked.</summary>
    public bool IsSelected { get; set; } = true;
}

/// <summary>One token of the original filename, marked as kept or removed by the cleaner.</summary>
public class DiffSegment
{
    public string Text { get; set; } = string.Empty;
    public bool IsRemoved { get; set; }
}

public partial class FilePreview : ObservableObject
{
    public string OriginalName { get; set; } = string.Empty;
    public string OriginalFilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int Year { get; set; }

    [ObservableProperty]
    private string cleanedName = string.Empty;

    [ObservableProperty]
    private double confidence;

    [ObservableProperty]
    private bool isReviewed;

    [ObservableProperty]
    private bool isSelected = true;

    [ObservableProperty]
    private bool hasWarning;

    [ObservableProperty]
    private string warningMessage = string.Empty;

    [ObservableProperty]
    private bool isDuplicate;

    [ObservableProperty]
    private bool isTvEpisode;

    private List<DiffSegment>? _diffSegmentsCache;

    /// <summary>Word-level diff: each token of the original name marked as kept or removed.</summary>
    public List<DiffSegment> OriginalDiffSegments => _diffSegmentsCache ??= ComputeDiff();

    partial void OnCleanedNameChanged(string value)
    {
        // Auto-trim on edit (D4). We bypass the public setter to avoid recursion.
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed != value)
        {
#pragma warning disable MVVMTK0034 // intentional: avoid setter recursion
            cleanedName = trimmed;
#pragma warning restore MVVMTK0034
            OnPropertyChanged(nameof(CleanedName));
            return;  // OnCleanedNameChanged will re-run with the trimmed value
        }

        // Invalidate diff cache so it recomputes against the new cleaned name
        _diffSegmentsCache = null;
        OnPropertyChanged(nameof(OriginalDiffSegments));
    }

    /// <summary>Returns "Low" / "Medium" / "High" based on Confidence score.</summary>
    public string ConfidenceLabel => Confidence switch
    {
        >= 0.80 => "High",
        >= 0.50 => "Medium",
        _ => "Low"
    };

    public string FileSizeDisplay
    {
        get
        {
            if (FileSizeBytes >= 1L << 30) return $"{FileSizeBytes / (double)(1L << 30):F1} GB";
            if (FileSizeBytes >= 1L << 20) return $"{FileSizeBytes / (double)(1L << 20):F1} MB";
            if (FileSizeBytes >= 1L << 10) return $"{FileSizeBytes / (double)(1L << 10):F1} KB";
            return $"{FileSizeBytes} B";
        }
    }

    private List<DiffSegment> ComputeDiff()
    {
        if (string.IsNullOrEmpty(OriginalName))
            return new List<DiffSegment>();

        // Collect tokens of the cleaned name (lowercased) so we can quickly check
        // whether each token of the original survived the cleanup.
        var cleanedTokens = new HashSet<string>(
            System.Text.RegularExpressions.Regex
                .Split(CleanedName ?? string.Empty, @"[\s\._\-\(\)\[\]\{\}]+")
                .Where(t => !string.IsNullOrEmpty(t)),
            StringComparer.OrdinalIgnoreCase);

        var segments = new List<DiffSegment>();
        // Split original PRESERVING separators so we can re-render them between tokens
        var parts = System.Text.RegularExpressions.Regex.Split(
            OriginalName, @"([\s\._\-\(\)\[\]\{\}]+)");

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            bool isSep = System.Text.RegularExpressions.Regex.IsMatch(
                part, @"^[\s\._\-\(\)\[\]\{\}]+$");
            bool isRemoved = !isSep && !cleanedTokens.Contains(part);
            segments.Add(new DiffSegment { Text = part, IsRemoved = isRemoved });
        }
        return segments;
    }
}

public class FolderPreview
{
    public string FolderName { get; set; } = string.Empty;
    public List<string> FilesInFolder { get; set; } = new();
}

public class ScrapingProgressItem
{
    public string MovieName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "Searching...", "Downloading...", "Complete", "Failed"
    public string? PosterUrl { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public partial class MovieFolderItem : ObservableObject
{
    public string FolderPath { get; set; } = string.Empty;
    public string MovieTitle { get; set; } = string.Empty;
    public int Year { get; set; }

    [ObservableProperty]
    private bool isSelected = true;

    [ObservableProperty]
    private string status = "Ready";

    [ObservableProperty]
    private bool isScraping = false;

    [ObservableProperty]
    private bool isScraped = false;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    public string DisplayName => Year > 0 ? $"{MovieTitle} ({Year})" : MovieTitle;
}
