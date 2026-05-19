using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

public class ScraperService
{
    private readonly TmdbApiClient _tmdbClient;
    private readonly ImageDownloadService _imageService;
    private readonly NfoGeneratorService _nfoService;
    private readonly MediaProbeService _mediaProbe;

    public ScraperService(string tmdbApiKey, string language = "en")
    {
        _tmdbClient = new TmdbApiClient(tmdbApiKey, language);
        _imageService = new ImageDownloadService();
        _nfoService = new NfoGeneratorService();
        _mediaProbe = new MediaProbeService();
    }

    /// <summary>
    /// Searches TMDb by title (and optional year), then downloads metadata for the FIRST result.
    /// Use this for unattended/bulk scraping when the user trusts the auto-match.
    /// </summary>
    public async Task<(bool success, string message)> ScrapeAndDownloadMetadataAsync(
        string movieFolderPath,
        string movieTitle,
        int movieYear,
        IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Searching for '{movieTitle}'...");
            var searchResults = await _tmdbClient.SearchMovieAsync(movieTitle, movieYear);

            if (searchResults.Count == 0)
                return (false, $"No results found for '{movieTitle}'");

            var movie = searchResults.FirstOrDefault();
            if (movie == null)
                return (false, "No matching movie found");

            return await DownloadAllAssetsAsync(movieFolderPath, movie.TmdbId, progress);
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads metadata for a SPECIFIC TMDb id (no search). Use this when the
    /// user has already picked the correct match in the TMDb search dialog.
    /// </summary>
    public async Task<(bool success, string message)> ScrapeByTmdbIdAsync(
        string movieFolderPath,
        int tmdbId,
        IProgress<string>? progress = null)
    {
        try
        {
            return await DownloadAllAssetsAsync(movieFolderPath, tmdbId, progress);
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Shared pipeline: details → cast → poster → fanart → actor photos → NFO file.
    /// </summary>
    private async Task<(bool success, string message)> DownloadAllAssetsAsync(
        string movieFolderPath,
        int tmdbId,
        IProgress<string>? progress)
    {
        // Details — single batched call now returns cast + crew + certification + trailer.
        progress?.Report("Downloading metadata...");
        var details = await _tmdbClient.GetMovieDetailsAsync(tmdbId);
        if (details == null)
            return (false, "Failed to get movie details");

        // Find a video file in the folder to base poster/fanart filenames on
        var videoFile = Directory.GetFiles(movieFolderPath, "*.*")
            .FirstOrDefault(f => Utilities.FileFormatValidator.IsVideoFile(f));

        // Edition tag detection — read straight off the video filename (Director's
        // Cut / Extended / IMAX / 4K Remaster / etc.). Surfaces in the NFO as
        // <edition> which Kodi/Plex/Jellyfin all recognise.
        if (!string.IsNullOrEmpty(videoFile))
        {
            var detected = Utilities.RegexPatterns.DetectEdition(Path.GetFileName(videoFile));
            if (!string.IsNullOrEmpty(detected))
                details.Edition = detected;
        }

        // Poster — pull TMDb's "original" upload (typically 1500–2000px tall).
        // The previous w500 default produced blurry results on any modern display.
        if (!string.IsNullOrEmpty(details.PosterPath) && !string.IsNullOrEmpty(videoFile))
        {
            progress?.Report("Downloading poster...");
            var posterUrl = _tmdbClient.GetImageUrl(details.PosterPath, "original");
            await _imageService.DownloadPosterAsync(posterUrl, videoFile);
        }

        // Fanart — also "original" (typically 1920×1080+) for sharp 1080p/4K TVs.
        if (!string.IsNullOrEmpty(details.BackdropPath) && !string.IsNullOrEmpty(videoFile))
        {
            progress?.Report("Downloading fanart...");
            var fanartUrl = _tmdbClient.GetImageUrl(details.BackdropPath, "original");
            await _imageService.DownloadFanartAsync(fanartUrl, videoFile);
        }

        // Actor photos
        progress?.Report("Downloading actor photos...");
        var actorsFolder = Path.Combine(movieFolderPath, ".actors");
        await _imageService.DownloadActorPhotosAsync(details.Cast, actorsFolder);

        // Probe the actual file for <fileinfo>/<streamdetails> — duration, video codec,
        // audio tracks with language, subtitle tracks. This is what lets the companion
        // viewer (and MediaElch / Plex / Kodi / Jellyfin) show the full detail strip.
        StreamDetails? streamDetails = null;
        if (!string.IsNullOrEmpty(videoFile))
        {
            progress?.Report("Probing media file...");
            streamDetails = _mediaProbe.Probe(videoFile);
        }

        // NFO
        progress?.Report("Generating metadata file...");
        _nfoService.SaveNfoFile(details, movieFolderPath, streamDetails);

        progress?.Report("Complete");
        return (true, $"Metadata scraped for '{details.Title}'");
    }
}
