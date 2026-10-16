using System.Xml.Linq;

namespace CineLibraryEssentials.Services;

/// <summary>
/// "Refresh IMDb ratings": updates the IMDb rating + votes inside already-scraped
/// NFOs without re-scraping anything else. Only the &lt;ratings&gt; block changes.
/// </summary>
public class ImdbRefreshService
{
    private readonly ImdbRatingService _imdb;

    public ImdbRefreshService(ImdbRatingService? imdb = null) => _imdb = imdb ?? new ImdbRatingService();

    public record Result(int Updated, int AlreadyCurrent, int NoImdbId, int NoRating, int NoNfo, int Failed);

    public async Task<Result> RefreshAsync(IEnumerable<string> folders, bool preferImdb, CancellationToken ct = default)
    {
        int noNfo = 0, noId = 0;
        var byId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);   // imdb id -> NFO paths

        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var nfo = NfoLocator.FindMainNfo(folder);
            if (nfo == null) { noNfo++; continue; }

            var id = ReadImdbId(nfo);
            if (id == null) { noId++; continue; }

            if (!byId.TryGetValue(id, out var paths)) byId[id] = paths = new List<string>();
            paths.Add(nfo);
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
                    if (RatingsXml.ApplyImdb(doc.Root!, r.Rating, r.Votes, preferImdb))
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

    /// <summary>IMDb id from &lt;uniqueid type="imdb"&gt;, falling back to a "tt…" &lt;id&gt;.</summary>
    public static string? ReadImdbId(string nfoPath)
    {
        try
        {
            var root = XDocument.Load(nfoPath).Root;
            if (root == null) return null;
            var fromUnique = root.Elements("uniqueid")
                .FirstOrDefault(u => string.Equals((string?)u.Attribute("type"), "imdb", StringComparison.OrdinalIgnoreCase))
                ?.Value.Trim();
            if (ImdbRatingService.IsImdbId(fromUnique)) return fromUnique;
            var legacy = root.Element("id")?.Value.Trim();
            return ImdbRatingService.IsImdbId(legacy) ? legacy : null;
        }
        catch
        {
            return null;
        }
    }
}
