using System.Xml.Linq;

namespace CineLibraryEssentials.Services;

/// <summary>
/// "Refresh IMDb ratings": updates the IMDb rating + votes inside already-scraped
/// NFOs without re-scraping anything else. Only the &lt;ratings&gt; block changes,
/// plus the IMDb id for NFOs that only had a TMDb id.
/// </summary>
public class ImdbRefreshService
{
    private readonly ImdbRatingService _imdb;
    private readonly Func<int, bool, CancellationToken, Task<string?>>? _findImdbId;

    /// <param name="findImdbId">
    /// Looks up the IMDb id of a TMDb id (movie, or show when the bool is true),
    /// for NFOs written before 1.1.10. Without it those are counted as having no IMDb id.
    /// </param>
    public ImdbRefreshService(ImdbRatingService? imdb = null,
        Func<int, bool, CancellationToken, Task<string?>>? findImdbId = null)
    {
        _imdb = imdb ?? new ImdbRatingService();
        _findImdbId = findImdbId;
    }

    public record Result(int Updated, int AlreadyCurrent, int NoImdbId, int NoRating, int NoNfo, int Failed);

    public async Task<Result> RefreshAsync(IEnumerable<string> folders, bool preferImdb, CancellationToken ct = default,
        IProgress<(int Done, int Total)>? lookupProgress = null)
    {
        int noNfo = 0, noId = 0;
        var byId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);   // imdb id -> NFO paths
        var needId = new List<(string Nfo, int TmdbId, bool Tv)>();

        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var nfo = NfoLocator.FindMainNfo(folder);
            if (nfo == null) { noNfo++; continue; }

            var (id, tmdbId, tv) = ReadIds(nfo);
            if (id == null)
            {
                if (tmdbId > 0 && _findImdbId != null) needId.Add((nfo, tmdbId, tv));
                else noId++;
                continue;
            }
            Add(byId, id, nfo);
        }

        // NFOs written before 1.1.10 have only the TMDb id: TMDb knows the IMDb one
        var addedId = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // NFO paths
        for (int i = 0; i < needId.Count; i++)
        {
            lookupProgress?.Report((i + 1, needId.Count));
            var (nfo, tmdbId, tv) = needId[i];
            var id = (await _findImdbId!(tmdbId, tv, ct))?.Trim();
            if (!ImdbRatingService.IsImdbId(id)) { noId++; continue; }
            Add(byId, id!, nfo);
            addedId.Add(nfo);
        }

        // One request per 100 titles, however big the library is.
        var ratings = await _imdb.GetRatingsAsync(byId.Keys, ct);

        int updated = 0, current = 0, noRating = 0, failed = 0;
        foreach (var (id, paths) in byId)
        {
            foreach (var nfo in paths)
            {
                if (!ratings.TryGetValue(id, out var r)) { noRating++; continue; }
                try
                {
                    var doc = NfoFile.Load(nfo);
                    bool changed = RatingsXml.ApplyImdb(doc.Root!, r.Rating, r.Votes, preferImdb);
                    if (addedId.Contains(nfo)) changed |= AddImdbId(doc.Root!, id);
                    if (changed)
                    {
                        NfoFile.Save(doc, nfo);
                        updated++;
                    }
                    else current++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"IMDb refresh failed for {nfo}: {ex.Message}");
                    failed++;
                }
            }
        }
        return new Result(updated, current, noId, noRating, noNfo, failed);
    }

    private static void Add(Dictionary<string, List<string>> byId, string id, string nfo)
    {
        if (!byId.TryGetValue(id, out var paths)) byId[id] = paths = new List<string>();
        paths.Add(nfo);
    }

    /// <summary>IMDb id from &lt;uniqueid type="imdb"&gt;, falling back to a "tt…" &lt;id&gt;.</summary>
    public static string? ReadImdbId(string nfoPath) => ReadIds(nfoPath).ImdbId;

    /// <summary>
    /// The NFO's IMDb id (as <see cref="ReadImdbId"/>), its TMDb id
    /// (&lt;uniqueid type="tmdb"&gt;, or the &lt;tmdbid&gt; NFOs before 1.1.10 have; 0 if none)
    /// and whether it's a show's.
    /// </summary>
    public static (string? ImdbId, int TmdbId, bool Tv) ReadIds(string nfoPath)
    {
        try
        {
            var root = XDocument.Load(nfoPath).Root;
            if (root == null) return (null, 0, false);
            var tv = root.Name.LocalName == "tvshow";

            var fromUnique = UniqueId(root, "imdb");
            var legacy = root.Element("id")?.Value.Trim();
            var imdb = ImdbRatingService.IsImdbId(fromUnique) ? fromUnique
                     : ImdbRatingService.IsImdbId(legacy) ? legacy : null;

            int.TryParse(UniqueId(root, "tmdb") ?? root.Element("tmdbid")?.Value.Trim(), out var tmdb);
            return (imdb, Math.Max(tmdb, 0), tv);
        }
        catch
        {
            return (null, 0, false);
        }
    }

    private static string? UniqueId(XElement root, string type) => root.Elements("uniqueid")
        .FirstOrDefault(u => string.Equals((string?)u.Attribute("type"), type, StringComparison.OrdinalIgnoreCase))
        ?.Value.Trim();

    /// <summary>
    /// Adds &lt;uniqueid type="imdb"&gt; next to the other ids, when the NFO has no
    /// IMDb id. True if it changed.
    /// </summary>
    public static bool AddImdbId(XElement root, string imdbId)
    {
        if (ImdbRatingService.IsImdbId(UniqueId(root, "imdb"))) return false;
        var id = new XElement("uniqueid", new XAttribute("type", "imdb"), imdbId);
        var after = root.Elements("uniqueid").LastOrDefault() ?? root.Element("tmdbid");
        if (after != null) after.AddAfterSelf(id);
        else if (root.Element("ratings") is { } ratings) ratings.AddAfterSelf(id);
        else root.Add(id);
        return true;
    }
}
