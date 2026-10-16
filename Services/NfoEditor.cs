using System.Globalization;
using System.Xml.Linq;

namespace CineLibraryEssentials.Services;

/// <summary>
/// The NFO editor. Reads the editable fields from a movie or tvshow NFO and
/// writes back only the ones the user changed, so every other tag (stream
/// details, ratings, tags from other tools) stays exactly as it was. Saving can
/// add &lt;lockdata&gt;true&lt;/lockdata&gt;, which Fill gaps / Scrape Selected
/// respect (and Jellyfin understands too).
/// </summary>
public static class NfoEditor
{
    /// <summary>One cast member. Thumb is the NFO's &lt;thumb&gt; text (".actors/Name.jpg" or a URL).</summary>
    public record Actor(string Name, string Role, string Thumb);

    public record Fields
    {
        public string Title { get; init; } = string.Empty;
        public string OriginalTitle { get; init; } = string.Empty;
        public string SortTitle { get; init; } = string.Empty;
        public int Year { get; init; }
        public string Premiered { get; init; } = string.Empty;
        public int Runtime { get; init; }
        public string Tagline { get; init; } = string.Empty;
        public string Plot { get; init; } = string.Empty;
        public string Outline { get; init; } = string.Empty;
        public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Studios { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Countries { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Directors { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Writers { get; init; } = Array.Empty<string>();
        public IReadOnlyList<Actor> Cast { get; init; } = Array.Empty<Actor>();
        public string AgeRating { get; init; } = string.Empty;
        public int UserRating { get; init; }
        public string Collection { get; init; } = string.Empty;
        public string Edition { get; init; } = string.Empty;
        public string Trailer { get; init; } = string.Empty;
        public string ImdbId { get; init; } = string.Empty;
        public string TmdbId { get; init; } = string.Empty;
    }

    public static Fields Read(XElement root) => new()
    {
        Title = Text(root, "title"),
        OriginalTitle = Text(root, "originaltitle"),
        SortTitle = Text(root, "sorttitle"),
        Year = Int(root, "year"),
        Premiered = Text(root, "premiered"),
        Runtime = Int(root, "runtime"),
        Tagline = Text(root, "tagline"),
        Plot = Text(root, "plot"),
        Outline = Text(root, "outline"),
        Genres = List(root, "genre"),
        Tags = List(root, "tag"),
        Studios = List(root, "studio"),
        Countries = List(root, "country"),
        Directors = List(root, "director"),
        Writers = List(root, "credits"),
        Cast = root.Elements("actor")
            .Select(a => new Actor(Child(a, "name"), Child(a, "role"), Child(a, "thumb")))
            .Where(a => a.Name.Length > 0)
            .ToList(),
        AgeRating = Text(root, "mpaa"),
        UserRating = Int(root, "userrating"),
        Collection = CollectionName(root),
        Edition = Text(root, "edition"),
        Trailer = Text(root, "trailer"),
        ImdbId = UniqueId(root, "imdb"),
        TmdbId = UniqueId(root, "tmdb"),
    };

    /// <summary>
    /// Writes the fields that differ between <paramref name="before"/> and
    /// <paramref name="after"/>, and sets or clears the lock. Returns true if
    /// the NFO changed.
    /// </summary>
    public static bool Apply(XElement root, Fields before, Fields after, bool lockData)
    {
        bool changed = false;

        void Single(string name, string oldValue, string newValue)
        {
            if (oldValue == newValue) return;
            SetText(root, name, newValue);
            changed = true;
        }
        void Many(string name, IReadOnlyList<string> oldValues, IReadOnlyList<string> newValues)
        {
            if (oldValues.SequenceEqual(newValues)) return;
            SetList(root, name, newValues);
            changed = true;
        }

        if (before.Title != after.Title)
        {
            // A show's <showtitle> normally repeats its title; keep them in step
            var showTitle = root.Element("showtitle");
            if (showTitle != null && showTitle.Value.Trim() == before.Title)
                showTitle.Value = after.Title.Trim();
        }
        if (before.Premiered != after.Premiered)
        {
            // Step 3 also writes the old <released> alias; keep it matching
            var released = root.Element("released");
            if (released != null) released.Value = after.Premiered.Trim();
        }

        Single("title", before.Title, after.Title);
        Single("originaltitle", before.OriginalTitle, after.OriginalTitle);
        Single("sorttitle", before.SortTitle, after.SortTitle);
        Single("year", Number(before.Year), Number(after.Year));
        Single("premiered", before.Premiered, after.Premiered);
        Single("runtime", Number(before.Runtime), Number(after.Runtime));
        Single("tagline", before.Tagline, after.Tagline);
        Single("plot", before.Plot, after.Plot);
        Single("outline", before.Outline, after.Outline);
        Many("genre", before.Genres, after.Genres);
        Many("tag", before.Tags, after.Tags);
        Many("studio", before.Studios, after.Studios);
        Many("country", before.Countries, after.Countries);
        Many("director", before.Directors, after.Directors);
        Many("credits", before.Writers, after.Writers);
        Single("mpaa", before.AgeRating, after.AgeRating);
        Single("userrating", Number(before.UserRating), Number(after.UserRating));
        Single("edition", before.Edition, after.Edition);
        Single("trailer", before.Trailer, after.Trailer);

        if (before.ImdbId != after.ImdbId)
        {
            SetUniqueId(root, "imdb", after.ImdbId);
            // Older tools read the plain <id>; keep it in step when it held the IMDb id
            var legacy = root.Element("id");
            if (legacy != null && legacy.Value.Trim() == before.ImdbId)
            {
                if (after.ImdbId.Trim().Length > 0) legacy.Value = after.ImdbId.Trim();
                else legacy.Remove();
            }
            changed = true;
        }
        if (before.TmdbId != after.TmdbId)
        {
            SetUniqueId(root, "tmdb", after.TmdbId);
            changed = true;
        }

        if (before.Collection != after.Collection)
        {
            SetCollection(root, after.Collection);
            changed = true;
        }

        if (!before.Cast.SequenceEqual(after.Cast))
        {
            SetCast(root, after.Cast);
            changed = true;
        }

        if (IsLocked(root) != lockData)
        {
            SetText(root, "lockdata", lockData ? "true" : string.Empty);
            changed = true;
        }
        return changed;
    }

    /// <summary>How many fields differ (for the editor's "N unsaved changes").</summary>
    public static int CountChanges(Fields a, Fields b)
    {
        int n = 0;
        void S(string x, string y) { if (x != y) n++; }
        void L<T>(IReadOnlyList<T> x, IReadOnlyList<T> y) { if (!x.SequenceEqual(y)) n++; }
        S(a.Title, b.Title); S(a.OriginalTitle, b.OriginalTitle); S(a.SortTitle, b.SortTitle);
        if (a.Year != b.Year) n++;
        S(a.Premiered, b.Premiered);
        if (a.Runtime != b.Runtime) n++;
        S(a.Tagline, b.Tagline); S(a.Plot, b.Plot); S(a.Outline, b.Outline);
        L(a.Genres, b.Genres); L(a.Tags, b.Tags); L(a.Studios, b.Studios); L(a.Countries, b.Countries);
        L(a.Directors, b.Directors); L(a.Writers, b.Writers); L(a.Cast, b.Cast);
        S(a.AgeRating, b.AgeRating);
        if (a.UserRating != b.UserRating) n++;
        S(a.Collection, b.Collection); S(a.Edition, b.Edition); S(a.Trailer, b.Trailer);
        S(a.ImdbId, b.ImdbId); S(a.TmdbId, b.TmdbId);
        return n;
    }

    // ---- Episodes (TV) ---------------------------------------------------

    public record EpisodeFields(string Title, string Aired, string Plot);

    public static EpisodeFields ReadEpisode(XElement root)
        => new(Text(root, "title"), Text(root, "aired"), Text(root, "plot"));

    /// <summary>Writes the changed episode fields. Returns true if the NFO changed.</summary>
    public static bool ApplyEpisode(XElement root, EpisodeFields before, EpisodeFields after)
    {
        bool changed = false;
        if (before.Title != after.Title) { SetText(root, "title", after.Title); changed = true; }
        if (before.Aired != after.Aired) { SetText(root, "aired", after.Aired); changed = true; }
        if (before.Plot != after.Plot) { SetText(root, "plot", after.Plot); changed = true; }
        return changed;
    }

    // ---- Bulk edit -------------------------------------------------------

    /// <summary>
    /// One change applied to many NFOs. Null means "leave as is"; an empty
    /// Collection clears it. Genres and tags are added and removed per item,
    /// so each title keeps the ones the change doesn't mention.
    /// </summary>
    public record BulkChange
    {
        public string? Collection { get; init; }
        public string? AgeRating { get; init; }
        public IReadOnlyList<string> AddGenres { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> RemoveGenres { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> AddTags { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> RemoveTags { get; init; } = Array.Empty<string>();
        public bool? Lock { get; init; }
    }

    public static Fields ApplyBulk(Fields f, BulkChange c) => f with
    {
        Collection = c.Collection?.Trim() ?? f.Collection,
        AgeRating = c.AgeRating?.Trim() ?? f.AgeRating,
        Genres = AddRemove(f.Genres, c.AddGenres, c.RemoveGenres),
        Tags = AddRemove(f.Tags, c.AddTags, c.RemoveTags),
    };

    private static IReadOnlyList<string> AddRemove(IReadOnlyList<string> current, IReadOnlyList<string> add, IReadOnlyList<string> remove)
    {
        var result = current.Where(v => !remove.Contains(v, StringComparer.OrdinalIgnoreCase)).ToList();
        foreach (var a in add)
            if (!result.Contains(a, StringComparer.OrdinalIgnoreCase)) result.Add(a.Trim());
        return result.SequenceEqual(current) ? current : result;
    }

    // ---- Lock ------------------------------------------------------------

    /// <summary>True when the NFO says &lt;lockdata&gt;true&lt;/lockdata&gt;.</summary>
    public static bool IsLocked(XElement root)
        => string.Equals(root.Element("lockdata")?.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a folder's main NFO is locked, and its TMDb id. Automatic scrapes
    /// use the id to stay on the user's chosen title, and leave a locked NFO
    /// (and existing images) alone.
    /// </summary>
    public static (bool Locked, int? TmdbId) LockInfo(string folder)
    {
        var nfo = NfoLocator.FindMainNfo(folder);
        if (nfo == null) return (false, null);
        try
        {
            var root = XDocument.Load(nfo).Root;
            if (root == null || !IsLocked(root)) return (false, null);
            var id = UniqueId(root, "tmdb");
            return (true, int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null);
        }
        catch
        {
            return (false, null);   // unreadable NFO: nothing to protect
        }
    }

    /// <summary>Splits a comma-separated value into clean values.</summary>
    public static IReadOnlyList<string> SplitList(string text)
        => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .ToList();

    // ---- helpers -------------------------------------------------------

    private static string Text(XElement root, string name) => root.Element(name)?.Value.Trim() ?? string.Empty;
    private static string Child(XElement e, string name) => e.Element(name)?.Value.Trim() ?? string.Empty;

    private static int Int(XElement root, string name)
        => int.TryParse(Text(root, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static IReadOnlyList<string> List(XElement root, string name)
        => root.Elements(name).Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();

    private static string Number(int n) => n > 0 ? n.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string UniqueId(XElement root, string type)
        => root.Elements("uniqueid")
               .FirstOrDefault(u => string.Equals((string?)u.Attribute("type"), type, StringComparison.OrdinalIgnoreCase))
               ?.Value.Trim() ?? string.Empty;

    // Collection is <set><name>X</name><overview/></set>; very old NFOs use <set>X</set>.
    private static string CollectionName(XElement root)
    {
        var set = root.Element("set");
        if (set == null) return string.Empty;
        return (set.Element("name")?.Value ?? (set.HasElements ? string.Empty : set.Value)).Trim();
    }

    /// <summary>Sets the first &lt;name&gt; element (removing extras), or removes it when empty.</summary>
    private static void SetText(XElement root, string name, string value)
    {
        value = value.Trim();
        var existing = root.Elements(name).ToList();
        if (value.Length == 0)
        {
            existing.Remove();
            return;
        }
        if (existing.Count == 0)
        {
            root.Add(new XElement(name, value));
            return;
        }
        existing[0].Value = value;
        existing.Skip(1).Remove();
    }

    /// <summary>Replaces every &lt;name&gt; element, keeping them where the first one was.</summary>
    private static void SetList(XElement root, string name, IReadOnlyList<string> values)
    {
        var existing = root.Elements(name).ToList();
        var fresh = values.Select(v => new XElement(name, v.Trim())).ToList();
        if (existing.Count > 0) existing[0].AddBeforeSelf(fresh);
        else root.Add(fresh);
        existing.Remove();
    }

    private static void SetUniqueId(XElement root, string type, string value)
    {
        value = value.Trim();
        var el = root.Elements("uniqueid")
            .FirstOrDefault(u => string.Equals((string?)u.Attribute("type"), type, StringComparison.OrdinalIgnoreCase));
        if (value.Length == 0) { el?.Remove(); return; }
        if (el != null) { el.Value = value; return; }

        var fresh = new XElement("uniqueid", new XAttribute("type", type), value);
        var last = root.Elements("uniqueid").LastOrDefault();
        if (last != null) last.AddAfterSelf(fresh); else root.Add(fresh);
    }

    private static void SetCollection(XElement root, string name)
    {
        name = name.Trim();
        var set = root.Element("set");
        if (name.Length == 0)
        {
            set?.Remove();
            return;
        }
        // A new collection name makes the old collection overview wrong, so it goes
        var fresh = new XElement("set", new XElement("name", name));
        if (set != null) set.ReplaceWith(fresh);
        else root.Add(fresh);
    }

    /// <summary>
    /// Rewrites the &lt;actor&gt; list in the new order. An actor that was
    /// already there keeps any extra tags other tools added (tmdbid, profile...).
    /// </summary>
    private static void SetCast(XElement root, IReadOnlyList<Actor> cast)
    {
        var existing = root.Elements("actor").ToList();
        var byName = existing
            .GroupBy(a => Child(a, "name"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var fresh = new List<XElement>();
        for (int i = 0; i < cast.Count; i++)
        {
            var a = cast[i];
            var el = byName.TryGetValue(a.Name.Trim(), out var old) ? new XElement(old) : new XElement("actor");
            SetChild(el, "name", a.Name);
            SetChild(el, "role", a.Role);
            SetChild(el, "order", i.ToString(CultureInfo.InvariantCulture));
            SetChild(el, "thumb", a.Thumb);
            fresh.Add(el);
        }

        if (existing.Count > 0) existing[0].AddBeforeSelf(fresh);
        else root.Add(fresh);
        existing.Remove();
    }

    private static void SetChild(XElement parent, string name, string value)
    {
        value = value.Trim();
        var el = parent.Element(name);
        if (value.Length == 0 && name == "thumb") { el?.Remove(); return; }
        if (el != null) el.Value = value;
        else parent.Add(new XElement(name, value));
    }
}
