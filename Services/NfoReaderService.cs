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
        /// <summary>True when read from a show's tvshow.nfo.</summary>
        public bool IsTvShow { get; set; }
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
        /// <summary>Every source in &lt;ratings&gt; (IMDb, TMDb, ...), main one first.</summary>
        public List<RatingsXml.Entry> Ratings { get; set; } = new();

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

        /// <summary>Chip text for one rating source, e.g. "IMDb ★ 8.8 · 2.6M votes".</summary>
        public static string RatingLabel(RatingsXml.Entry r)
        {
            var source = r.Name switch
            {
                RatingsXml.Imdb => "IMDb",
                RatingsXml.Tmdb => "TMDb",
                "" => string.Empty,
                _ => char.ToUpperInvariant(r.Name[0]) + r.Name[1..],
            };
            var votes = r.Votes switch
            {
                >= 1_000_000 => $" · {r.Votes / 1_000_000.0:0.#}M votes",
                >= 1_000 => $" · {r.Votes / 1_000.0:0.#}K votes",
                > 0 => $" · {r.Votes} votes",
                _ => string.Empty,
            };
            return $"{source} ★ {r.Value:F1}{votes}".Trim();
        }
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

    /// <summary>One episode on disk, for the details window's episode list.</summary>
    public record EpisodeEntry(int Season, int Episode, int LastEpisode, string Title, string Aired,
        string? ThumbPath, string VideoFile);

    /// <summary>
    /// The show's episodes on disk, sorted, with title and air date from each
    /// episode's .nfo (falling back to the filename) and its -thumb.jpg.
    /// </summary>
    public static List<EpisodeEntry> ReadEpisodes(string showFolder)
    {
        var list = new List<EpisodeEntry>();
        foreach (var f in TvEpisodeScanner.Scan(showFolder))
        {
            var basePath = Path.Combine(Path.GetDirectoryName(f.VideoFile)!, Path.GetFileNameWithoutExtension(f.VideoFile));
            string title = string.Empty, aired = string.Empty;
            try
            {
                if (File.Exists(basePath + ".nfo"))
                {
                    var root = XDocument.Load(basePath + ".nfo").Root;
                    if (root != null)
                    {
                        title = Read(root, "title");
                        aired = Read(root, "aired");
                    }
                }
            }
            catch { /* unreadable NFO: fall back to the filename */ }

            if (string.IsNullOrEmpty(title)) title = Path.GetFileNameWithoutExtension(f.VideoFile);
            var thumb = basePath + "-thumb.jpg";
            list.Add(new EpisodeEntry(f.Season, f.Episode, f.LastEpisode, title, aired,
                File.Exists(thumb) ? thumb : null, f.VideoFile));
        }
        return list.OrderBy(e => e.Season).ThenBy(e => e.Episode).ToList();
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

        // Prefer tvshow.nfo, never an episode's NFO (see NfoLocator).
        var nfoFile = NfoLocator.FindMainNfo(folderPath);
        if (nfoFile == null) return null;
        var isTvShow = Path.GetFileName(nfoFile).Equals("tvshow.nfo", StringComparison.OrdinalIgnoreCase);

        try
        {
            var doc = XDocument.Load(nfoFile);
            var root = doc.Root;
            if (root == null) return null;

            var detail = new MovieDetail { FolderPath = folderPath, IsTvShow = isTvShow };

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
                detail.Ratings = RatingsXml.ReadAll(root)
                    .OrderByDescending(r => r.IsDefault)
                    .ToList();
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
            // TV shows use plain "poster.jpg" / "fanart.jpg" at the show root (the
            // root may also hold "season01-poster.jpg", so check the plain name
            // first); movies use "<basename>-poster.jpg".
            detail.PosterPath = ResolveLocalImage(folderPath, root.Element("poster")?.Value, "-poster.jpg",
                                                  isTvShow ? "poster.jpg" : null);
            detail.FanartPath = ResolveLocalImage(folderPath, root.Element("fanart")?.Value, "-fanart.jpg",
                                                  isTvShow ? "fanart.jpg" : null);

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

    private static string? ResolveLocalImage(string folderPath, string? nfoValue, string suffix, string? plainName = null)
    {
        if (!string.IsNullOrWhiteSpace(nfoValue))
        {
            var candidate = Path.IsPathRooted(nfoValue) ? nfoValue : Path.Combine(folderPath, nfoValue);
            if (File.Exists(candidate)) return candidate;
        }
        if (plainName != null)
        {
            var plain = Path.Combine(folderPath, plainName);
            if (File.Exists(plain)) return plain;
        }
        // Fallback: first file ending in the suffix
        var match = Directory.EnumerateFiles(folderPath)
            .FirstOrDefault(f => f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        return match;
    }
}
