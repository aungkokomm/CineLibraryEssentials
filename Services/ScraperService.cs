using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

public class ScraperService
{
    private readonly TmdbApiClient _tmdbClient;
    private readonly ImageDownloadService _imageService;
    private readonly NfoGeneratorService _nfoService;

    public ScraperService(string tmdbApiKey)
    {
        _tmdbClient = new TmdbApiClient(tmdbApiKey);
        _imageService = new ImageDownloadService();
        _nfoService = new NfoGeneratorService();
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
        // Details
        progress?.Report("Downloading metadata...");
        var details = await _tmdbClient.GetMovieDetailsAsync(tmdbId);
        if (details == null)
            return (false, "Failed to get movie details");

        // Cast
        progress?.Report("Downloading cast information...");
        var cast = await _tmdbClient.GetMovieCastAsync(tmdbId);
        details.Cast = cast;

        // Find a video file in the folder to base poster/fanart filenames on
        var videoFile = Directory.GetFiles(movieFolderPath, "*.*")
            .FirstOrDefault(f => Utilities.FileFormatValidator.IsVideoFile(f));

        // Poster
        if (!string.IsNullOrEmpty(details.PosterPath) && !string.IsNullOrEmpty(videoFile))
        {
            progress?.Report("Downloading poster...");
            var posterUrl = _tmdbClient.GetImageUrl(details.PosterPath);
            await _imageService.DownloadPosterAsync(posterUrl, videoFile);
        }

        // Fanart
        if (!string.IsNullOrEmpty(details.BackdropPath) && !string.IsNullOrEmpty(videoFile))
        {
            progress?.Report("Downloading fanart...");
            var fanartUrl = _tmdbClient.GetImageUrl(details.BackdropPath, "w1280");
            await _imageService.DownloadFanartAsync(fanartUrl, videoFile);
        }

        // Actor photos
        progress?.Report("Downloading actor photos...");
        var actorsFolder = Path.Combine(movieFolderPath, ".actors");
        await _imageService.DownloadActorPhotosAsync(details.Cast, actorsFolder);

        // NFO
        progress?.Report("Generating metadata file...");
        _nfoService.SaveNfoFile(details, movieFolderPath);

        progress?.Report("Complete");
        return (true, $"Metadata scraped for '{details.Title}'");
    }
}
