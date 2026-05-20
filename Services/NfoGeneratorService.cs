using System.Xml.Linq;
using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

/// <summary>
/// Generates a Kodi-standard movie .nfo file that matches what MediaElch writes —
/// so any player or library manager (Plex, Kodi, Jellyfin, Emby, MediaElch, the
/// companion CineLibrary viewer) reads a consistent, fully-populated record.
/// </summary>
public class NfoGeneratorService
{
    public string GenerateNfoXml(
        MovieMetadata metadata,
        string movieFolderPath,
        StreamDetails? streamDetails = null)
    {
        var folderBase = Path.GetFileNameWithoutExtension(Path.GetFileName(movieFolderPath));

        var root = new XElement("movie");

        // ---- Titles ----
        AddText(root, "title", metadata.Title);
        if (!string.IsNullOrWhiteSpace(metadata.OriginalTitle)
            && !string.Equals(metadata.OriginalTitle, metadata.Title, StringComparison.Ordinal))
        {
            AddText(root, "originaltitle", metadata.OriginalTitle);
        }

        // ---- Ratings (Kodi <ratings> block — supports multiple sources) ----
        if (metadata.Rating > 0)
        {
            var ratings = new XElement("ratings",
                new XElement("rating",
                    new XAttribute("name", "themoviedb"),
                    new XAttribute("default", "true"),
                    new XAttribute("max", "10"),
                    new XElement("value", metadata.Rating.ToString("F1")),
                    new XElement("votes", metadata.VoteCount)));
            root.Add(ratings);
        }

        AddText(root, "userrating", "0");
        AddText(root, "top250", "0");

        // ---- Story ----
        AddText(root, "outline", metadata.Overview);
        AddText(root, "plot", metadata.Overview);
        AddText(root, "tagline", metadata.Tagline);

        if (metadata.Runtime > 0)
            AddText(root, "runtime", metadata.Runtime.ToString());

        AddText(root, "mpaa", metadata.Certification);
        AddText(root, "playcount", "0");
        AddText(root, "lastplayed", string.Empty);

        // ---- IDs ----
        // Legacy <id> tag (still read by older Kodi versions and many tools)
        if (!string.IsNullOrEmpty(metadata.ImdbId))
            AddText(root, "id", metadata.ImdbId);

        // Modern <uniqueid> tags — IMDb gets default=true when present, else TMDb does.
        var hasImdb = !string.IsNullOrEmpty(metadata.ImdbId);
        if (hasImdb)
        {
            root.Add(new XElement("uniqueid",
                new XAttribute("type", "imdb"),
                new XAttribute("default", "true"),
                metadata.ImdbId!));
        }
        if (metadata.TmdbId > 0)
        {
            var uniq = new XElement("uniqueid",
                new XAttribute("type", "tmdb"),
                metadata.TmdbId.ToString());
            if (!hasImdb) uniq.Add(new XAttribute("default", "true"));
            root.Add(uniq);
        }

        // ---- Genres ----
        foreach (var g in metadata.Genres)
            if (!string.IsNullOrWhiteSpace(g.Name))
                root.Add(new XElement("genre", g.Name));

        // ---- Countries ----
        foreach (var c in metadata.ProductionCountries)
            if (!string.IsNullOrWhiteSpace(c.Name))
                root.Add(new XElement("country", c.Name));

        // ---- Collection / Set ----
        if (metadata.BelongsToCollection != null
            && !string.IsNullOrWhiteSpace(metadata.BelongsToCollection.Name))
        {
            root.Add(new XElement("set",
                new XElement("name", metadata.BelongsToCollection.Name),
                new XElement("overview", metadata.BelongsToCollection.Overview ?? string.Empty)));
        }

        // ---- Crew ----
        foreach (var w in metadata.Writers)
            if (!string.IsNullOrWhiteSpace(w.Name))
                root.Add(new XElement("credits", w.Name));

        foreach (var d in metadata.Directors)
            if (!string.IsNullOrWhiteSpace(d.Name))
                root.Add(new XElement("director", d.Name));

        // ---- Dates ----
        if (!string.IsNullOrEmpty(metadata.ReleaseDate))
        {
            AddText(root, "premiered", metadata.ReleaseDate);
            AddText(root, "released", metadata.ReleaseDate); // legacy alias
        }
        if (metadata.Year > 0)
            AddText(root, "year", metadata.Year.ToString());

        // ---- Studios ----
        foreach (var s in metadata.ProductionCompanies)
            if (!string.IsNullOrWhiteSpace(s.Name))
                root.Add(new XElement("studio", s.Name));

        // ---- Trailer ----
        AddText(root, "trailer", metadata.TrailerUrl);

        // ---- Edition (Kodi-standard; Director's Cut / Extended / IMAX / …) ----
        AddText(root, "edition", metadata.Edition);

        // ---- Local image refs (kept after fields the spec lists earlier) ----
        AddText(root, "poster", $"{folderBase}-poster.jpg");
        AddText(root, "fanart", $"{folderBase}-fanart.jpg");

        // ---- File / stream details ----
        if (streamDetails != null)
        {
            var fileInfo = BuildFileInfoElement(streamDetails);
            if (fileInfo != null) root.Add(fileInfo);
        }

        // ---- Actors (one element each, with <thumb>) ----
        foreach (var actorElement in BuildActorElements(metadata.Cast))
            root.Add(actorElement);

        // ---- dateadded (when this NFO was written) ----
        root.Add(new XElement("dateadded",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            root).ToString();
    }

    public void SaveNfoFile(
        MovieMetadata metadata,
        string movieFolderPath,
        StreamDetails? streamDetails = null)
    {
        try
        {
            var nfoContent = GenerateNfoXml(metadata, movieFolderPath, streamDetails);
            var nfoFileName = $"{Path.GetFileNameWithoutExtension(Path.GetFileName(movieFolderPath))}.nfo";
            var nfoPath = Path.Combine(movieFolderPath, nfoFileName);
            File.WriteAllText(nfoPath, nfoContent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving NFO file: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------

    private static void AddText(XElement parent, string name, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        parent.Add(new XElement(name, value));
    }

    private static XElement? BuildFileInfoElement(StreamDetails sd)
    {
        var hasVideo = sd.Video.DurationInSeconds > 0
                       || sd.Video.Width > 0
                       || !string.IsNullOrEmpty(sd.Video.Codec);
        if (!hasVideo && sd.AudioTracks.Count == 0 && sd.SubtitleTracks.Count == 0)
            return null;

        var streamdetails = new XElement("streamdetails");

        // <video>
        var video = new XElement("video");
        AddText(video, "codec", sd.Video.Codec);
        AddText(video, "aspect", sd.Video.Aspect);
        if (sd.Video.Width > 0) AddText(video, "width", sd.Video.Width.ToString());
        if (sd.Video.Height > 0) AddText(video, "height", sd.Video.Height.ToString());
        if (sd.Video.DurationInSeconds > 0)
            AddText(video, "durationinseconds", sd.Video.DurationInSeconds.ToString());
        if (video.HasElements) streamdetails.Add(video);

        // <audio> per track
        foreach (var a in sd.AudioTracks)
        {
            var audio = new XElement("audio");
            AddText(audio, "codec", a.Codec);
            AddText(audio, "language", a.Language);
            if (a.Channels > 0) AddText(audio, "channels", a.Channels.ToString());
            if (audio.HasElements) streamdetails.Add(audio);
        }

        // <subtitle> per track
        foreach (var s in sd.SubtitleTracks)
        {
            var sub = new XElement("subtitle");
            AddText(sub, "language", s.Language);
            // Some libraries expect <subtitle> even when language is unknown.
            if (!sub.HasElements) sub.Add(new XElement("language", string.Empty));
            streamdetails.Add(sub);
        }

        return new XElement("fileinfo", streamdetails);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  TV SHOWS — tvshow.nfo (show root) + episodedetails .nfo (per episode)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kodi-standard tvshow.nfo content. Goes at the show's root folder
    /// alongside the Season XX/ subfolders.
    /// </summary>
    public string GenerateTvShowNfoXml(TvShowMetadata show)
    {
        var root = new XElement("tvshow");

        AddText(root, "title", show.Name);
        if (!string.IsNullOrWhiteSpace(show.OriginalName)
            && !string.Equals(show.OriginalName, show.Name, StringComparison.Ordinal))
            AddText(root, "originaltitle", show.OriginalName);

        if (show.Rating > 0)
        {
            root.Add(new XElement("ratings",
                new XElement("rating",
                    new XAttribute("name", "themoviedb"),
                    new XAttribute("default", "true"),
                    new XAttribute("max", "10"),
                    new XElement("value", show.Rating.ToString("F1")),
                    new XElement("votes", show.VoteCount))));
        }

        AddText(root, "userrating", "0");
        AddText(root, "plot", show.Overview);
        AddText(root, "tagline", show.Tagline);
        AddText(root, "mpaa", show.ContentRating);
        if (show.EpisodeRunTime > 0)
            AddText(root, "runtime", show.EpisodeRunTime.ToString());

        // IDs
        if (!string.IsNullOrEmpty(show.ImdbId))
        {
            AddText(root, "id", show.ImdbId);
            root.Add(new XElement("uniqueid",
                new XAttribute("type", "imdb"),
                new XAttribute("default", "true"),
                show.ImdbId));
        }
        if (show.TmdbId > 0)
        {
            var uniq = new XElement("uniqueid",
                new XAttribute("type", "tmdb"),
                show.TmdbId.ToString());
            if (string.IsNullOrEmpty(show.ImdbId))
                uniq.Add(new XAttribute("default", "true"));
            root.Add(uniq);
        }

        // Genres / Countries / Studios / Networks
        foreach (var g in show.Genres)               AddText(root, "genre", g.Name);
        foreach (var c in show.ProductionCountries)  AddText(root, "country", c.Name);
        foreach (var s in show.ProductionCompanies)  AddText(root, "studio", s.Name);
        foreach (var n in show.Networks)             AddText(root, "studio", n.Name);

        // Creators get written as <credits> (Kodi convention for TV writers/creators)
        foreach (var c in show.Creators)
            if (!string.IsNullOrWhiteSpace(c.Name)) AddText(root, "credits", c.Name);

        if (!string.IsNullOrEmpty(show.FirstAirDate))
        {
            AddText(root, "premiered", show.FirstAirDate);
            AddText(root, "year", show.Year > 0 ? show.Year.ToString() : string.Empty);
        }

        AddText(root, "status", show.Status);

        // Local image refs at the show root
        AddText(root, "poster", "poster.jpg");
        AddText(root, "fanart", "fanart.jpg");

        // Actors (same shape as movies — one <actor> per cast member with <thumb>)
        foreach (var actor in BuildActorElements(show.Cast))
            root.Add(actor);

        root.Add(new XElement("dateadded", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).ToString();
    }

    /// <summary>
    /// Kodi-standard episodedetails .nfo for a single episode — sits next to the
    /// episode video file. Filename convention: "&lt;same as video&gt;.nfo".
    /// </summary>
    public string GenerateEpisodeNfoXml(
        TvEpisodeMetadata episode,
        TvShowMetadata show,
        string videoFileName,
        StreamDetails? streamDetails = null)
    {
        var root = new XElement("episodedetails");

        AddText(root, "title", string.IsNullOrEmpty(episode.Name)
            ? $"Episode {episode.EpisodeNumber}"
            : episode.Name);
        AddText(root, "showtitle", show.Name);
        AddText(root, "season", episode.SeasonNumber.ToString());
        AddText(root, "episode", episode.EpisodeNumber.ToString());

        if (episode.Rating > 0)
        {
            root.Add(new XElement("ratings",
                new XElement("rating",
                    new XAttribute("name", "themoviedb"),
                    new XAttribute("default", "true"),
                    new XAttribute("max", "10"),
                    new XElement("value", episode.Rating.ToString("F1")),
                    new XElement("votes", episode.VoteCount))));
        }

        AddText(root, "plot", episode.Overview);
        if (episode.Runtime > 0) AddText(root, "runtime", episode.Runtime.ToString());
        AddText(root, "aired", episode.AirDate);
        AddText(root, "mpaa", show.ContentRating);

        if (episode.TmdbId > 0)
        {
            root.Add(new XElement("uniqueid",
                new XAttribute("type", "tmdb"),
                new XAttribute("default", "true"),
                episode.TmdbId.ToString()));
        }

        // Episode thumbnail (sits next to the video file as "<videoBase>-thumb.jpg")
        var thumbName = Path.GetFileNameWithoutExtension(videoFileName) + "-thumb.jpg";
        AddText(root, "thumb", thumbName);

        if (streamDetails != null)
        {
            var fileInfo = BuildFileInfoElement(streamDetails);
            if (fileInfo != null) root.Add(fileInfo);
        }

        // Reuse the show-level cast for the episode (TMDb's per-episode guest_stars
        // would be more precise but adds API calls; show-level cast is the
        // common pattern Plex/Kodi accept).
        foreach (var actor in BuildActorElements(show.Cast))
            root.Add(actor);

        root.Add(new XElement("dateadded", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).ToString();
    }

    public void SaveTvShowNfo(TvShowMetadata show, string showFolderPath)
    {
        try
        {
            var content = GenerateTvShowNfoXml(show);
            File.WriteAllText(Path.Combine(showFolderPath, "tvshow.nfo"), content);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving tvshow.nfo: {ex.Message}");
        }
    }

    public void SaveEpisodeNfo(
        TvEpisodeMetadata episode,
        TvShowMetadata show,
        string episodeVideoPath,
        StreamDetails? streamDetails = null)
    {
        try
        {
            var content = GenerateEpisodeNfoXml(
                episode, show,
                Path.GetFileName(episodeVideoPath),
                streamDetails);
            var nfoPath = Path.ChangeExtension(episodeVideoPath, ".nfo");
            File.WriteAllText(nfoPath, content);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving episode nfo: {ex.Message}");
        }
    }

    /// <summary>
    /// One sibling &lt;actor&gt; per cast member with &lt;name&gt;, &lt;role&gt;,
    /// &lt;order&gt;, and a &lt;thumb&gt; ref pointing to the local .actors/ photo.
    /// </summary>
    private static IEnumerable<XElement> BuildActorElements(List<CastMember> cast)
    {
        for (int i = 0; i < cast.Count; i++)
        {
            var member = cast[i];
            var actor = new XElement("actor",
                new XElement("name", member.Name),
                new XElement("role", member.Character),
                new XElement("order", i));

            if (!string.IsNullOrEmpty(member.ProfilePath))
            {
                var fileName = ImageDownloadService.BuildActorFileName(member.Name);
                actor.Add(new XElement("thumb", $".actors/{fileName}"));
            }

            yield return actor;
        }
    }
}
