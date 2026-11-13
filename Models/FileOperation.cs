using CommunityToolkit.Mvvm.ComponentModel;

namespace CineLibraryEssentials.Models;

// Step 2's operations and result, apart from the WinUI-bound models so the
// unit tests can compile them

public class ProcessingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();

    /// <summary>What Step 2 moved, in order, so Undo can put it back.</summary>
    public List<FileMove> Moves { get; } = new();

    /// <summary>The folders Step 2 made, so Undo can take them away again once they're empty.</summary>
    public List<string> CreatedFolders { get; } = new();
}

/// <summary>One file or folder Step 2 moved.</summary>
public record FileMove(string From, string To, bool IsFolder);

public partial class FileOperation : ObservableObject
{
    public string OriginalFilePath { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string CleanedTitle { get; set; } = string.Empty;
    public int Year { get; set; }
    public double Confidence { get; set; }

    [ObservableProperty]
    private string destinationFolder = string.Empty;

    public string FinalFileName { get; set; } = string.Empty;

    /// <summary>Detected edition tag (e.g. "Director's Cut", "IMAX"), empty if none.</summary>
    public string Edition { get; set; } = string.Empty;

    // ---- TV episode metadata (populated only for TV episodes) ----
    public string ShowName { get; set; } = string.Empty;
    public int Season { get; set; }
    public int Episode { get; set; }
    public string EpisodeTitle { get; set; } = string.Empty;
    public bool IsTvEpisode => Season > 0 && Episode > 0 && !string.IsNullOrEmpty(ShowName);

    /// <summary>Whether this operation will be executed when "Run File to Folder" is clicked.</summary>
    [ObservableProperty]
    private bool isSelected = true;
}

public class FolderPreview
{
    public string FolderName { get; set; } = string.Empty;
    public List<string> FilesInFolder { get; set; } = new();
}
