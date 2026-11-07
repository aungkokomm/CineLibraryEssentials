namespace CineLibraryEssentials.Models;

public class AppConfig
{
    // ===== Existing =====
    public string? TmdbApiKey { get; set; }
    public string LastOutputPath { get; set; } = string.Empty;
    public List<string> SupportedFormats { get; set; } = new();

    // ===== v1.1 — persistent settings =====
    /// <summary>Recent source folders (most recent first, max 10).</summary>
    public List<string> RecentSourceFolders { get; set; } = new();

    /// <summary>Recent output folders (most recent first, max 10).</summary>
    public List<string> RecentOutputFolders { get; set; } = new();

    /// <summary>Window dimensions remembered between sessions.</summary>
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 800;
    public int WindowX { get; set; } = -1;  // -1 = let OS center
    public int WindowY { get; set; } = -1;

    /// <summary>IDs of warnings/tips that the user has dismissed permanently.</summary>
    public List<string> DismissedWarnings { get; set; } = new();

    /// <summary>"Grid" or "List" preference for Step 3.</summary>
    public string PreferredStep3View { get; set; } = "Grid";

    /// <summary>Last-used output filename template.</summary>
    public string LastTemplate { get; set; } = "{Title} ({Year})";

    /// <summary>Whether the "clean embedded metadata" checkbox should default ON.</summary>
    public bool CleanEmbeddedMetadata { get; set; } = false;

    /// <summary>Sort key for Step 1's file list.</summary>
    public string Step1SortColumn { get; set; } = "Confidence";
    public bool Step1SortDescending { get; set; } = true;

    // ----- Auto-update check (v1.1.10) -----
    /// <summary>UTC ticks of the last successful GitHub update check, 0 = never.</summary>
    public long LastUpdateCheckUtcTicks { get; set; }
    /// <summary>Version the user clicked "Skip" on — don't bug them about it again until a newer one ships.</summary>
    public string SkippedUpdateVersion { get; set; } = string.Empty;

    // ----- Settings page (v1.1.11) -----
    /// <summary>ISO 639-1 language code for TMDb scrapes ("en", "my", "hi", …).</summary>
    public string ScrapeLanguage { get; set; } = "en";
    /// <summary>Whether the silent once-per-24h GitHub update check runs on startup.</summary>
    public bool AutoCheckForUpdates { get; set; } = true;
    /// <summary>Default "Recursive subfolders" toggle state in Step 1's source picker.</summary>
    public bool RecursiveScanDefault { get; set; } = false;

    // ----- Wizard mode (v1.2) -----
    /// <summary>"Auto" / "Movies" / "TvShows" — controls how Step 1 parses + Step 2 lays out.</summary>
    public string WizardMode { get; set; } = "Auto";

    // ----- v1.3 -----
    /// <summary>User-defined words/tags Step 1 always strips from filenames (e.g. "ION10", "mSD").</summary>
    public List<string> CustomRemoveWords { get; set; } = new();

    /// <summary>When true (default), the IMDb rating is the NFO's main rating when known; else TMDb.</summary>
    public bool PreferImdbRating { get; set; } = true;

    /// <summary>Opt-in: after a TV scrape, add the episode title to file names that don't have one.</summary>
    public bool AddEpisodeTitlesToFilenames { get; set; }

    // ----- v1.4 -----
    /// <summary>Where the details and editor windows were last left, by window name.</summary>
    public Dictionary<string, WindowBounds> WindowPlacements { get; set; } = new();

    // NFO and pictures (Settings). Defaults write and download everything, as before.
    /// <summary>How many actors a scrape writes (and fetches photos for); 0 = all.</summary>
    public int CastLimit { get; set; }
    /// <summary>Write only the first studio (for TV: the first network, else studio).</summary>
    public bool FirstStudioOnly { get; set; }
    /// <summary>Write only the first director, writer and (TV) creator.</summary>
    public bool FirstCrewOnly { get; set; }
    /// <summary>TMDb sizes to download ("original", "w500", ...).</summary>
    public string PosterSize { get; set; } = "original";
    public string FanartSize { get; set; } = "original";
    public string ThumbSize { get; set; } = "original";
    public string ActorSize { get; set; } = "original";
    public string LogoSize { get; set; } = "original";
}

/// <summary>A window's last size and position (the restored ones when it was maximized).</summary>
public class WindowBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}
