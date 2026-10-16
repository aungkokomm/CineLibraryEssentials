using System.Globalization;
using System.Xml.Linq;

namespace CineLibraryEssentials.Services;

/// <summary>
/// The Kodi &lt;ratings&gt; block, which can hold one entry per source:
///   &lt;ratings&gt;
///     &lt;rating name="imdb" max="10" default="true"&gt;&lt;value&gt;7.8&lt;/value&gt;&lt;votes&gt;512000&lt;/votes&gt;&lt;/rating&gt;
///     &lt;rating name="themoviedb" max="10"&gt;&lt;value&gt;7.4&lt;/value&gt;&lt;votes&gt;9120&lt;/votes&gt;&lt;/rating&gt;
///   &lt;/ratings&gt;
/// Exactly one entry is marked default; that's the rating players show first.
/// </summary>
public static class RatingsXml
{
    public const string Imdb = "imdb";
    public const string Tmdb = "themoviedb";

    public record Entry(string Name, double Value, int Votes, bool IsDefault);

    /// <summary>
    /// Builds the block for a new NFO, or null when there's no rating at all.
    /// IMDb is the default when <paramref name="preferImdb"/> and it's known.
    /// </summary>
    public static XElement? Build(double tmdb, int tmdbVotes, double imdb, int imdbVotes, bool preferImdb)
    {
        if (tmdb <= 0 && imdb <= 0) return null;
        var imdbIsDefault = imdb > 0 && (preferImdb || tmdb <= 0);

        var ratings = new XElement("ratings");
        // Default entry first, so tools that just read the first <rating> agree with players.
        if (imdbIsDefault)
        {
            ratings.Add(Rating(Imdb, imdb, imdbVotes, true));
            if (tmdb > 0) ratings.Add(Rating(Tmdb, tmdb, tmdbVotes, false));
        }
        else
        {
            if (tmdb > 0) ratings.Add(Rating(Tmdb, tmdb, tmdbVotes, true));
            if (imdb > 0) ratings.Add(Rating(Imdb, imdb, imdbVotes, false));
        }
        return ratings;
    }

    /// <summary>
    /// Updates the IMDb entry of an existing NFO in place (for "Refresh IMDb
    /// ratings"). Keeps every other source and every other tag untouched.
    /// Returns true if the NFO changed.
    /// </summary>
    public static bool ApplyImdb(XElement nfoRoot, double rating, int votes, bool preferImdb)
    {
        if (rating <= 0) return false;

        var existing = ReadAll(nfoRoot);
        var tmdb = existing.FirstOrDefault(e => e.Name == Tmdb);
        var old = existing.FirstOrDefault(e => e.Name == Imdb);
        if (old != null && Math.Abs(old.Value - Math.Round(rating, 1)) < 0.001 && old.Votes == votes
            && old.IsDefault == (preferImdb || tmdb == null))
            return false;   // already current

        // Rebuild the block from what's there plus the new IMDb value, keeping any
        // other sources (e.g. ones written by other tools) after ours.
        var others = existing.Where(e => e.Name != Imdb && e.Name != Tmdb).ToList();
        var block = Build(tmdb?.Value ?? 0, tmdb?.Votes ?? 0, rating, votes, preferImdb)!;
        foreach (var o in others) block.Add(Rating(o.Name, o.Value, o.Votes, false));

        var current = nfoRoot.Element("ratings");
        if (current != null)
        {
            current.ReplaceWith(block);
        }
        else
        {
            // Kodi convention puts <ratings> right after the titles.
            var anchor = nfoRoot.Element("originaltitle") ?? nfoRoot.Element("title");
            if (anchor != null) anchor.AddAfterSelf(block); else nfoRoot.AddFirst(block);
        }
        return true;
    }

    /// <summary>All entries in the NFO's &lt;ratings&gt; block (empty if none).</summary>
    public static List<Entry> ReadAll(XElement nfoRoot)
    {
        var list = new List<Entry>();
        var ratings = nfoRoot.Element("ratings");
        if (ratings == null) return list;

        foreach (var r in ratings.Elements("rating"))
        {
            var name = ((string?)r.Attribute("name") ?? string.Empty).Trim().ToLowerInvariant();
            if (!double.TryParse(r.Element("value")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || value <= 0)
                continue;
            int.TryParse(r.Element("votes")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var votes);
            var isDefault = string.Equals((string?)r.Attribute("default"), "true", StringComparison.OrdinalIgnoreCase);
            list.Add(new Entry(name, value, votes, isDefault));
        }
        return list;
    }

    private static XElement Rating(string name, double value, int votes, bool isDefault)
    {
        var el = new XElement("rating", new XAttribute("name", name));
        if (isDefault) el.Add(new XAttribute("default", "true"));
        el.Add(new XAttribute("max", "10"),
               new XElement("value", value.ToString("F1", CultureInfo.InvariantCulture)),
               new XElement("votes", votes.ToString(CultureInfo.InvariantCulture)));
        return el;
    }
}
