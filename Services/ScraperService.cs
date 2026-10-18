using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

public class ScraperService
{
    private readonly TmdbApiClient _tmdbClient;
    private readonly ImageDownloadService _imageService;
    private readonly NfoGeneratorService _nfoService;
    private readonly MediaProbeService _mediaProbe;
    private readonly ImdbRatingService _imdb = new();
    private readonly bool _addEpisodeTitles;

    public ScraperService(string tmdbApiKey, string language = "en", bool preferImdbRating = true,
        bool addEpisodeTitles = false)
    {
        _tmdbClient = new TmdbApiClient(tmdbApiKey, language);
        _imageService = new ImageDownloadService();
        _nfoService = new NfoGeneratorService { PreferImdbRating = preferImdbRating };
        _mediaProbe = new MediaProbeService();
        _addEpisodeTitles = addEpisodeTitles;
    }

    /// <summary>
    /// Outcome of a TV scrape. Missing = aired episodes not on disk; Renamed =
    /// episode files that got their title added to the name (for Undo).
    /// </summary>
    public record TvScrapeResult(bool Success, string Message, int Missing, List<EpisodeFileRenamer.Rename> Renamed)
    {
        public static TvScrapeResult Fail(string message) => new(false, message, 0, new());
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
            // Details the user edited and locked: stay on their TMDb id instead of
            // searching, and only fill in missing images (see keepEdits).
            var lockInfo = NfoEditor.LockInfo(movieFolderPath);
            if (lockInfo.TmdbId is int lockedId)
                return await DownloadAllAssetsAsync(movieFolderPath, lockedId, progress, keepEdits: true);

            // A movie scraped before stays on its TMDb match (perhaps picked with
            // Search TMDb) and keeps its pictures; only missing ones are added.
            if (NfoLocator.FindMainNfo(movieFolderPath) is { } nfo
                && ImdbRefreshService.ReadIds(nfo).TmdbId is var knownId and > 0)
                return await DownloadAllAssetsAsync(movieFolderPath, knownId, progress,
                    keepEdits: lockInfo.Locked, keepImages: true);

            progress?.Report($"Searching for '{movieTitle}'...");
            var searchResults = await _tmdbClient.SearchMovieAsync(movieTitle, movieYear);

            if (searchResults.Count == 0)
                return (false, $"No results found for '{movieTitle}'");

            var movie = searchResults.FirstOrDefault();
            if (movie == null)
                return (false, "No matching movie found");

            return await DownloadAllAssetsAsync(movieFolderPath, movie.TmdbId, progress, keepEdits: lockInfo.Locked);
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
    /// keepEdits (a locked NFO, automatic scrape): the NFO and any existing poster
    /// or fanart are left as the user made them; only missing images are added.
    /// keepImages (a rescrape): existing poster and fanart stay; the NFO is rewritten.
    /// </summary>
    private async Task<(bool success, string message)> DownloadAllAssetsAsync(
        string movieFolderPath,
        int tmdbId,
        IProgress<string>? progress,
        bool keepEdits = false,
        bool keepImages = false)
    {
        keepImages |= keepEdits;
        // Details — single batched call now returns cast + crew + certification + trailer.
        progress?.Report("Downloading metadata...");
        var details = await _tmdbClient.GetMovieDetailsAsync(tmdbId);
        if (details == null)
            return (false, "Failed to get movie details");

        // IMDb rating + votes (best effort: skipped quietly if unavailable)
        if (await _imdb.GetRatingAsync(details.ImdbId) is { } imdbRating)
            (details.ImdbRating, details.ImdbVotes) = (imdbRating.Rating, imdbRating.Votes);

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
        if (!string.IsNullOrEmpty(details.PosterPath) && !string.IsNullOrEmpty(videoFile)
            && !(keepImages && HasImage(movieFolderPath, "-poster.jpg")))
        {
            progress?.Report("Downloading poster...");
            var posterUrl = _tmdbClient.GetImageUrl(details.PosterPath, "original");
            await _imageService.DownloadPosterAsync(posterUrl, videoFile);
        }

        // Fanart — also "original" (typically 1920×1080+) for sharp 1080p/4K TVs.
        if (!string.IsNullOrEmpty(details.BackdropPath) && !string.IsNullOrEmpty(videoFile)
            && !(keepImages && HasImage(movieFolderPath, "-fanart.jpg")))
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

        // NFO (never over a locked one: those are the user's edits)
        if (keepEdits)
        {
            progress?.Report("Complete");
            return (true, $"Kept your edited details for '{details.Title}' and added any missing images");
        }
        progress?.Report("Generating metadata file...");
        _nfoService.SaveNfoFile(details, movieFolderPath, streamDetails);

        progress?.Report("Complete");
        return (true, $"Metadata scraped for '{details.Title}'");
    }

    private static bool HasImage(string folder, string suffix)
        => Directory.EnumerateFiles(folder, "*" + suffix).Any();

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
    public async Task<TvScrapeResult> ScrapeTvShowAsync(
        string showFolderPath,
        IProgress<string>? progress = null)
    {
        if (!Directory.Exists(showFolderPath))
            return TvScrapeResult.Fail($"Folder not found: {showFolderPath}");

        // 1) Find every episode file under the folder (handles BOTH layouts):
        //      a) Show/Season 01/Ep.mp4   — classic Kodi multi-season root
        //      b) Show Season 1 Complete/Ep.mp4   — flat single-season folder
        progress?.Report("Scanning for episodes...");
        var episodes = TvEpisodeScanner.Scan(showFolderPath);
        if (episodes.Count == 0)
            return TvScrapeResult.Fail("No TV episode files found in this folder.");

        // A show the user edited and locked keeps its TMDb id and its tvshow.nfo.
        var lockInfo = NfoEditor.LockInfo(showFolderPath);
        if (lockInfo.TmdbId is int lockedId)
            return await ScrapeTvShowWithDetailsAsync(showFolderPath, episodes, lockedId, progress, keepEdits: true);

        // A show scraped before stays on its TMDb match (perhaps picked with
        // Search TMDb); a name search could land on another show of that name.
        if (ImdbRefreshService.ReadIds(Path.Combine(showFolderPath, "tvshow.nfo")).TmdbId is var knownId and > 0)
            return await ScrapeTvShowWithDetailsAsync(showFolderPath, episodes, knownId, progress, keepEdits: lockInfo.Locked);

        // 2) Show name from filenames — far more reliable than the folder name
        //    (which is often "Show Name Season 1 Complete" or worse).
        var showName = PickConsensusShowName(episodes)
                       ?? Path.GetFileName(showFolderPath);

        progress?.Report($"Searching TMDb for '{showName}'...");
        var matches = await _tmdbClient.SearchTvAsync(showName);
        if (matches.Count == 0)
            return TvScrapeResult.Fail($"No TMDb match for '{showName}'");

        var top = matches.First();
        return await ScrapeTvShowWithDetailsAsync(showFolderPath, episodes, top.TmdbId, progress, keepEdits: lockInfo.Locked);
    }

    /// <summary>
    /// TV scrape variant that skips the auto-search — used after the user picks
    /// a specific show in the TMDb search dialog.
    /// </summary>
    public async Task<TvScrapeResult> ScrapeTvShowByIdAsync(
        string showFolderPath,
        int tmdbId,
        IProgress<string>? progress = null)
    {
        if (!Directory.Exists(showFolderPath))
            return TvScrapeResult.Fail($"Folder not found: {showFolderPath}");

        progress?.Report("Scanning for episodes...");
        var episodes = TvEpisodeScanner.Scan(showFolderPath);
        if (episodes.Count == 0)
            return TvScrapeResult.Fail("No TV episode files found in this folder.");

        return await ScrapeTvShowWithDetailsAsync(showFolderPath, episodes, tmdbId, progress);
    }

    /// <summary>
    /// Shared finisher used by both ScrapeTvShowAsync (auto-search) and
    /// ScrapeTvShowByIdAsync (user-picked id). Given the TMDb show id and the
    /// already-found list of local episode files, downloads metadata + assets
    /// and writes the NFOs. Also counts aired episodes that aren't on disk.
    /// keepEdits (a locked tvshow.nfo, automatic scrape): tvshow.nfo is left as
    /// the user edited it. Show images are never replaced anyway.
    /// </summary>
    private async Task<TvScrapeResult> ScrapeTvShowWithDetailsAsync(
        string showFolderPath,
        List<TvEpisodeScanner.EpisodeFile> episodes,
        int tmdbId,
        IProgress<string>? progress,
        bool keepEdits = false)
    {
        var details = await _tmdbClient.GetTvDetailsAsync(tmdbId);
        if (details == null)
            return TvScrapeResult.Fail($"Failed to fetch show details (tmdb id {tmdbId})");

        // IMDb rating + votes for the show (best effort: skipped quietly if unavailable)
        if (await _imdb.GetRatingAsync(details.ImdbId) is { } imdbRating)
            (details.ImdbRating, details.ImdbVotes) = (imdbRating.Rating, imdbRating.Votes);

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
        var seasonEpisodes = new Dictionary<int, List<TvEpisodeMetadata>>();   // for the missing check
        var renamed = new List<EpisodeFileRenamer.Rename>();
        foreach (var seasonGroup in episodes.GroupBy(e => e.Season).OrderBy(g => g.Key))
        {
            progress?.Report($"Loading season {seasonGroup.Key} from TMDb...");
            var tmdbEpisodes = await _tmdbClient.GetTvSeasonAsync(details.TmdbId, seasonGroup.Key);
            seasonEpisodes[seasonGroup.Key] = tmdbEpisodes;
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

                // Opt-in: "Show - S01E01.mkv" -> "Show - S01E01 - Pilot.mkv" before the
                // NFO and thumbnail are written, so they get the new name too.
                // Double episodes keep their name (the title format has one number).
                var videoFile = ep.VideoFile;
                if (_addEpisodeTitles && ep.LastEpisode == ep.Episode && string.IsNullOrWhiteSpace(ep.EpisodeTitle))
                {
                    var title = EpisodeFileRenamer.CleanTitle(tmdbEp.Name);
                    var show = string.IsNullOrWhiteSpace(ep.ShowName) ? details.Name : ep.ShowName;
                    if (title.Length > 0
                        && EpisodeFileRenamer.RenameWithSidecars(videoFile,
                               RenameService.BuildTvFileName(show, ep.Season, ep.Episode, title)) is { } newPath)
                    {
                        renamed.Add(new EpisodeFileRenamer.Rename(videoFile, newPath));
                        videoFile = newPath;
                    }
                }

                var streams = _mediaProbe.Probe(videoFile);
                _nfoService.SaveEpisodeNfo(tmdbEp, details, videoFile, streams);

                if (!string.IsNullOrEmpty(tmdbEp.StillPath))
                {
                    var thumbUrl = _tmdbClient.GetImageUrl(tmdbEp.StillPath, "original");
                    var thumbPath = Path.Combine(
                        Path.GetDirectoryName(videoFile)!,
                        Path.GetFileNameWithoutExtension(videoFile) + "-thumb.jpg");
                    if (!File.Exists(thumbPath))
                        await _imageService.DownloadAnyImageAsync(thumbUrl, thumbPath);
                }

                episodesScraped++;
            }
        }

        // 5) tvshow.nfo last so a mid-flight failure doesn't leave it pointing
        //    at a half-scraped show.
        if (!keepEdits)
        {
            progress?.Report("Writing tvshow.nfo...");
            _nfoService.SaveTvShowNfo(details, showFolderPath);
        }

        var missing = MissingEpisodes.Total(
            MissingEpisodes.Find(TvEpisodeScanner.Covered(episodes), details, seasonEpisodes));

        progress?.Report("Complete");
        return new TvScrapeResult(true,
            $"Scraped '{details.Name}' - {episodesScraped} episode(s)"
            + (episodesFailed > 0 ? $", {episodesFailed} failed" : ""),
            missing, renamed);
    }

    /// <summary>
    /// Picks the show name that appears most often across the parsed episodes.
    /// Avoids a single oddly-named file skewing the TMDb search.
    /// </summary>
    private static string? PickConsensusShowName(List<TvEpisodeScanner.EpisodeFile> episodes)
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
