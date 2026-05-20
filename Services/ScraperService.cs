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

    // ═════════════════════════════════════════════════════════════════════
    //  TV SHOW SCRAPING
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scrapes an entire TV show: searches TMDb by show name, downloads show-level
    /// metadata + poster + fanart + cast photos, then walks every Season XX/
    /// subfolder and writes a per-episode .nfo + thumbnail next to each video.
    /// The folder layout is the standard one Step 1/2 produces:
    ///   showFolderPath/
    ///       Season 01/
    ///           Show - S01E01 - Title.mp4
    ///           Show - S01E01 - Title.nfo        (written here)
    ///           Show - S01E01 - Title-thumb.jpg  (episode thumbnail)
    /// </summary>
    public async Task<(bool success, string message)> ScrapeTvShowAsync(
        string showFolderPath,
        IProgress<string>? progress = null)
    {
        if (!Directory.Exists(showFolderPath))
            return (false, $"Folder not found: {showFolderPath}");

        // 1) Find every episode file under the folder (handles BOTH layouts):
        //      a) Show/Season 01/Ep.mp4   — classic Kodi multi-season root
        //      b) Show Season 1 Complete/Ep.mp4   — flat single-season folder
        progress?.Report("Scanning for episodes...");
        var episodes = FindEpisodeFiles(showFolderPath);
        if (episodes.Count == 0)
            return (false, "No TV episode files found in this folder.");

        // 2) Show name from filenames — far more reliable than the folder name
        //    (which is often "Show Name Season 1 Complete" or worse).
        var showName = PickConsensusShowName(episodes)
                       ?? Path.GetFileName(showFolderPath);

        progress?.Report($"Searching TMDb for '{showName}'...");
        var matches = await _tmdbClient.SearchTvAsync(showName);
        if (matches.Count == 0)
            return (false, $"No TMDb match for '{showName}'");

        var top = matches.First();
        return await ScrapeTvShowWithDetailsAsync(showFolderPath, episodes, top.TmdbId, progress);
    }

    /// <summary>
    /// TV scrape variant that skips the auto-search — used after the user picks
    /// a specific show in the TMDb search dialog.
    /// </summary>
    public async Task<(bool success, string message)> ScrapeTvShowByIdAsync(
        string showFolderPath,
        int tmdbId,
        IProgress<string>? progress = null)
    {
        if (!Directory.Exists(showFolderPath))
            return (false, $"Folder not found: {showFolderPath}");

        progress?.Report("Scanning for episodes...");
        var episodes = FindEpisodeFiles(showFolderPath);
        if (episodes.Count == 0)
            return (false, "No TV episode files found in this folder.");

        return await ScrapeTvShowWithDetailsAsync(showFolderPath, episodes, tmdbId, progress);
    }

    /// <summary>
    /// Shared finisher used by both ScrapeTvShowAsync (auto-search) and
    /// ScrapeTvShowByIdAsync (user-picked id). Given the TMDb show id and the
    /// already-found list of local episode files, downloads metadata + assets
    /// and writes the NFOs.
    /// </summary>
    private async Task<(bool success, string message)> ScrapeTvShowWithDetailsAsync(
        string showFolderPath,
        List<EpisodeFile> episodes,
        int tmdbId,
        IProgress<string>? progress)
    {
        var details = await _tmdbClient.GetTvDetailsAsync(tmdbId);
        if (details == null)
            return (false, $"Failed to fetch show details (tmdb id {tmdbId})");

        // 3) Show-level images at the FOLDER root (works for either layout —
        //    flat single-season folders get poster/fanart there too).
        progress?.Report("Downloading show poster + fanart...");
        await DownloadTvShowImagesAsync(showFolderPath, details);

        progress?.Report($"Downloading actor photos ({details.Cast.Count})...");
        var actorsFolder = Path.Combine(showFolderPath, ".actors");
        await _imageService.DownloadActorPhotosAsync(details.Cast, actorsFolder);

        // 4) Group episodes by season number, fetch each season's metadata
        //    from TMDb in one call, then scrape every episode in that season.
        int episodesScraped = 0, episodesFailed = 0;
        foreach (var seasonGroup in episodes.GroupBy(e => e.Season).OrderBy(g => g.Key))
        {
            progress?.Report($"Loading season {seasonGroup.Key} from TMDb...");
            var tmdbEpisodes = await _tmdbClient.GetTvSeasonAsync(details.TmdbId, seasonGroup.Key);
            var byNumber = tmdbEpisodes.ToDictionary(e => e.EpisodeNumber);

            foreach (var ep in seasonGroup)
            {
                if (!byNumber.TryGetValue(ep.Episode, out var tmdbEp))
                {
                    episodesFailed++;
                    System.Diagnostics.Debug.WriteLine(
                        $"No TMDb episode for S{ep.Season:D2}E{ep.Episode:D2}");
                    continue;
                }

                progress?.Report($"Scraping S{ep.Season:D2}E{ep.Episode:D2}...");

                var streams = _mediaProbe.Probe(ep.VideoFile);
                _nfoService.SaveEpisodeNfo(tmdbEp, details, ep.VideoFile, streams);

                if (!string.IsNullOrEmpty(tmdbEp.StillPath))
                {
                    var thumbUrl = _tmdbClient.GetImageUrl(tmdbEp.StillPath, "original");
                    var thumbPath = Path.Combine(
                        Path.GetDirectoryName(ep.VideoFile)!,
                        Path.GetFileNameWithoutExtension(ep.VideoFile) + "-thumb.jpg");
                    if (!File.Exists(thumbPath))
                        await _imageService.DownloadAnyImageAsync(thumbUrl, thumbPath);
                }

                episodesScraped++;
            }
        }

        // 5) tvshow.nfo last so a mid-flight failure doesn't leave it pointing
        //    at a half-scraped show.
        progress?.Report("Writing tvshow.nfo...");
        _nfoService.SaveTvShowNfo(details, showFolderPath);

        progress?.Report("Complete");
        return (true,
            $"Scraped '{details.Name}' — {episodesScraped} episode(s)"
            + (episodesFailed > 0 ? $", {episodesFailed} failed" : ""));
    }

    private record EpisodeFile(string VideoFile, int Season, int Episode, string ShowName);

    /// <summary>
    /// Finds every video file under the folder (direct or in any subfolder one
    /// level deep — i.e. either flat layout OR Season XX/ subdirs) and parses
    /// the SxxExx out of each filename. Skips files without a TV pattern.
    /// </summary>
    private static List<EpisodeFile> FindEpisodeFiles(string rootFolder)
    {
        var list = new List<EpisodeFile>();
        try
        {
            // Direct files
            foreach (var f in Directory.EnumerateFiles(rootFolder))
                TryAddEpisode(f, list);

            // Files one level deep (Season XX/, or any subfolder)
            foreach (var sub in Directory.EnumerateDirectories(rootFolder))
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(sub))
                        TryAddEpisode(f, list);
                }
                catch { /* permissions */ }
            }
        }
        catch { /* permissions */ }
        return list;
    }

    private static void TryAddEpisode(string filePath, List<EpisodeFile> list)
    {
        if (!Utilities.FileFormatValidator.IsVideoFile(filePath)) return;
        var parse = Utilities.RegexPatterns.ParseTvEpisode(Path.GetFileName(filePath));
        if (parse == null) return;
        list.Add(new EpisodeFile(filePath, parse.Season, parse.Episode, parse.ShowName));
    }

    /// <summary>
    /// Picks the show name that appears most often across the parsed episodes.
    /// Avoids a single oddly-named file skewing the TMDb search.
    /// </summary>
    private static string? PickConsensusShowName(List<EpisodeFile> episodes)
    {
        var grouped = episodes
            .Where(e => !string.IsNullOrWhiteSpace(e.ShowName))
            .GroupBy(e => e.ShowName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();
        return grouped.Count == 0 ? null : grouped[0].Key;
    }

    /// <summary>Downloads poster.jpg + fanart.jpg at the show root.</summary>
    private async Task DownloadTvShowImagesAsync(string showFolderPath, TvShowMetadata details)
    {
        if (!string.IsNullOrEmpty(details.PosterPath))
        {
            var posterUrl = _tmdbClient.GetImageUrl(details.PosterPath, "original");
            var posterPath = Path.Combine(showFolderPath, "poster.jpg");
            if (!File.Exists(posterPath))
                await _imageService.DownloadAnyImageAsync(posterUrl, posterPath);
        }
        if (!string.IsNullOrEmpty(details.BackdropPath))
        {
            var fanartUrl = _tmdbClient.GetImageUrl(details.BackdropPath, "original");
            var fanartPath = Path.Combine(showFolderPath, "fanart.jpg");
            if (!File.Exists(fanartPath))
                await _imageService.DownloadAnyImageAsync(fanartUrl, fanartPath);
        }
    }

    private static int ParseSeasonNumber(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return 0;
        var m = System.Text.RegularExpressions.Regex.Match(
            folderName, @"Season[\s_\.]?(\d{1,3})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
    }
}
