using System.Xml.Linq;
using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

/// <summary>
/// Parses the .nfo XML file we (or MediaElch / Kodi) write into a typed
/// <see cref="MovieDetail"/> for the Movie Details dialog. Reads gracefully —
/// any missing element is skipped, never throws on a malformed file.
/// </summary>
public class NfoReaderService
{
    public class MovieDetail
    {
        public string FolderPath { get; set; } = string.Empty;
        public string? VideoFilePath { get; set; }
        public string? PosterPath { get; set; }
        public string? FanartPath { get; set; }

        public string Title { get; set; } = string.Empty;
        public string OriginalTitle { get; set; } = string.Empty;
        public string Tagline { get; set; } = string.Empty;
        public string Plot { get; set; } = string.Empty;
        public string Mpaa { get; set; } = string.Empty;
        public string Edition { get; set; } = string.Empty;
        public string Premiered { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Runtime { get; set; }
        public double Rating { get; set; }
        public int VoteCount { get; set; }

        public string ImdbId { get; set; } = string.Empty;
        public string TmdbId { get; set; } = string.Empty;
        public string TrailerUrl { get; set; } = string.Empty;

        public List<string> Genres { get; set; } = new();
        public List<string> Countries { get; set; } = new();
        public List<string> Studios { get; set; } = new();
        public List<string> Directors { get; set; } = new();
        public List<string> Writers { get; set; } = new();

        public string CollectionName { get; set; } = string.Empty;
        public string CollectionOverview { get; set; } = string.Empty;

        public List<ActorEntry> Actors { get; set; } = new();

        // Stream details (the strip that shows codec / duration / audio / subs)
        public string VideoCodec { get; set; } = string.Empty;
        public int VideoWidth { get; set; }
        public int VideoHeight { get; set; }
        public int DurationSeconds { get; set; }
        public List<string> AudioLanguages { get; set; } = new();
        public List<string> SubtitleLanguages { get; set; } = new();

        // ---- Convenience derived values ----
        public string DisplayName => Year > 0 ? $"{Title} ({Year})" : Title;
        public string RuntimeDisplay => Runtime > 0 ? $"{Runtime} min" : string.Empty;
        public string RatingDisplay => Rating > 0 ? $"★ {Rating:F1}" : string.Empty;
        public bool HasOriginalTitle =>
            !string.IsNullOrWhiteSpace(OriginalTitle)
            && !string.Equals(OriginalTitle, Title, StringComparison.Ordinal);
    }

    public class ActorEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string? PhotoPath { get; set; }
    }

    /// <summary>
    /// Reads the first .nfo file in the given folder and returns a populated
    /// MovieDetail (with local image paths resolved). Returns null if no .nfo
    /// exists or it can't be parsed.
    /// </summary>
    public MovieDetail? ReadFromFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return null;

        var nfoFile = Directory.EnumerateFiles(folderPath, "*.nfo").FirstOrDefault();
        if (nfoFile == null) return null;

        try
        {
            var doc = XDocument.Load(nfoFile);
            var root = doc.Root;
            if (root == null) return null;

            var detail = new MovieDetail { FolderPath = folderPath };

            detail.Title          = Read(root, "title");
            detail.OriginalTitle  = Read(root, "originaltitle");
            detail.Tagline        = Read(root, "tagline");
            detail.Plot           = Read(root, "plot");
            detail.Mpaa           = Read(root, "mpaa");
            detail.Edition        = Read(root, "edition");
            detail.Premiered      = Read(root, "premiered");
            if (string.IsNullOrEmpty(detail.Premiered)) detail.Premiered = Read(root, "released");
            detail.Year           = ReadInt(root, "year");
            detail.Runtime        = ReadInt(root, "runtime");
            detail.TrailerUrl     = Read(root, "trailer");

            // IDs — prefer modern <uniqueid type="..."> over legacy <id>
            foreach (var uid in root.Elements("uniqueid"))
            {
                var type = (string?)uid.Attribute("type") ?? string.Empty;
                var value = (uid.Value ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(value)) continue;
                if (type.Equals("imdb", StringComparison.OrdinalIgnoreCase)) detail.ImdbId = value;
                else if (type.Equals("tmdb", StringComparison.OrdinalIgnoreCase)) detail.TmdbId = value;
            }
            if (string.IsNullOrEmpty(detail.ImdbId)) detail.ImdbId = Read(root, "id");

            // Rating — either modern <ratings><rating><value/><votes/></rating></ratings>
            // or legacy <rating>X.X</rating> / <votes>N</votes>
            var ratingsEl = root.Element("ratings");
            if (ratingsEl != null)
            {
                var first = ratingsEl.Elements("rating").FirstOrDefault();
                if (first != null)
                {
                    if (double.TryParse(first.Element("value")?.Value ?? "0",
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var rv))
                        detail.Rating = rv;
                    if (int.TryParse(first.Element("votes")?.Value ?? "0", out var vc))
                        detail.VoteCount = vc;
                }
            }
            else
            {
                if (double.TryParse(Read(root, "rating"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var rv))
                    detail.Rating = rv;
                detail.VoteCount = ReadInt(root, "votes");
            }

            // Multi-valued tags
            detail.Genres    = root.Elements("genre")  .Select(e => e.Value.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            detail.Countries = root.Elements("country").Select(e => e.Value.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            detail.Studios   = root.Elements("studio") .Select(e => e.Value.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            detail.Directors = root.Elements("director").Select(e => e.Value.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            detail.Writers   = root.Elements("credits").Select(e => e.Value.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();

            // Collection / set
            var setEl = root.Element("set");
            if (setEl != null)
            {
                detail.CollectionName = setEl.Element("name")?.Value?.Trim() ?? string.Empty;
                detail.CollectionOverview = setEl.Element("overview")?.Value?.Trim() ?? string.Empty;
            }

            // Actors — one <actor> sibling per cast member (Kodi standard)
            foreach (var a in root.Elements("actor"))
            {
                var name = a.Element("name")?.Value?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                var role = a.Element("role")?.Value?.Trim() ?? string.Empty;

                // Resolve the local photo file. The <thumb> ref we write is relative
                // (e.g. ".actors/Robert_De_Niro.jpg"); fall back to building the name
                // ourselves so we still find photos for NFOs MediaElch wrote.
                string? photoPath = null;
                var thumb = a.Element("thumb")?.Value?.Trim();
                if (!string.IsNullOrEmpty(thumb))
                {
                    var candidate = Path.IsPathRooted(thumb) ? thumb : Path.Combine(folderPath, thumb);
                    if (File.Exists(candidate)) photoPath = candidate;
                }
                if (photoPath == null)
                {
                    var actorsDir = Path.Combine(folderPath, ".actors");
                    var guessed = Path.Combine(actorsDir, ImageDownloadService.BuildActorFileName(name));
                    if (File.Exists(guessed)) photoPath = guessed;
                }

                detail.Actors.Add(new ActorEntry { Name = name, Role = role, PhotoPath = photoPath });
            }

            // <fileinfo><streamdetails> — codec, duration, audio + subtitle tracks
            var streamEl = root.Element("fileinfo")?.Element("streamdetails");
            if (streamEl != null)
            {
                var videoEl = streamEl.Element("video");
                if (videoEl != null)
                {
                    detail.VideoCodec      = videoEl.Element("codec")?.Value?.Trim() ?? string.Empty;
                    detail.VideoWidth      = int.TryParse(videoEl.Element("width")?.Value, out var w) ? w : 0;
                    detail.VideoHeight     = int.TryParse(videoEl.Element("height")?.Value, out var h) ? h : 0;
                    detail.DurationSeconds = int.TryParse(videoEl.Element("durationinseconds")?.Value, out var d) ? d : 0;
                }

                foreach (var au in streamEl.Elements("audio"))
                {
                    var lang = au.Element("language")?.Value?.Trim();
                    if (!string.IsNullOrEmpty(lang)) detail.AudioLanguages.Add(lang);
                }
                foreach (var sub in streamEl.Elements("subtitle"))
                {
                    var lang = sub.Element("language")?.Value?.Trim();
                    if (!string.IsNullOrEmpty(lang)) detail.SubtitleLanguages.Add(lang);
                }
            }

            // Resolve local image paths. NFO references like "<basename>-poster.jpg"
            // are relative to the folder; fall back to scanning for any *-poster.jpg
            // / *-fanart.jpg if the exact name doesn't exist.
            detail.PosterPath = ResolveLocalImage(folderPath, root.Element("poster")?.Value, "-poster.jpg");
            detail.FanartPath = ResolveLocalImage(folderPath, root.Element("fanart")?.Value, "-fanart.jpg");

            // Find the video file (first supported extension in the folder)
            detail.VideoFilePath = Directory.EnumerateFiles(folderPath)
                .FirstOrDefault(f => Utilities.FileFormatValidator.IsVideoFile(f));

            return detail;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NfoReaderService.ReadFromFolder failed for {folderPath}: {ex.Message}");
            return null;
        }
    }

    private static string Read(XElement root, string name)
        => root.Element(name)?.Value?.Trim() ?? string.Empty;

    private static int ReadInt(XElement root, string name)
    {
        var s = Read(root, name);
        return int.TryParse(s, out var v) ? v : 0;
    }

    private static string? ResolveLocalImage(string folderPath, string? nfoValue, string suffix)
    {
        if (!string.IsNullOrWhiteSpace(nfoValue))
        {
            var candidate = Path.IsPathRooted(nfoValue) ? nfoValue : Path.Combine(folderPath, nfoValue);
            if (File.Exists(candidate)) return candidate;
        }
        // Fallback: first file ending in the suffix
        var match = Directory.EnumerateFiles(folderPath)
            .FirstOrDefault(f => f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        return match;
    }
}
