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
