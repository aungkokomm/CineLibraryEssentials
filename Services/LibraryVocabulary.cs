using System.Xml.Linq;

namespace CineLibraryEssentials.Services;

/// <summary>
/// "Suggestions while typing" for the NFO editor: the genres, tags, studios,
/// countries, people and collections already used in the NFOs of the folders
/// listed in Step 3, so names stay consistent across the library.
/// </summary>
public sealed class LibraryVocabulary
{
    public IReadOnlyList<string> Genres { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<string> Tags { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<string> Studios { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<string> Countries { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<string> People { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<string> Collections { get; private init; } = Array.Empty<string>();

    public static LibraryVocabulary Empty { get; } = new();

    /// <summary>Reads every folder's main NFO. Unreadable files are skipped.</summary>
    public static LibraryVocabulary Collect(IEnumerable<string> folders)
    {
        var sets = new Dictionary<string, SortedSet<string>>();
        SortedSet<string> S(string key)
            => sets.TryGetValue(key, out var s) ? s : sets[key] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var nfo = NfoLocator.FindMainNfo(folder);
                if (nfo == null) continue;
                var root = XDocument.Load(nfo).Root;
                if (root == null) continue;

                var f = NfoEditor.Read(root);
                foreach (var v in f.Genres) S("genre").Add(v);
                foreach (var v in f.Tags) S("tag").Add(v);
                foreach (var v in f.Studios) S("studio").Add(v);
                foreach (var v in f.Countries) S("country").Add(v);
                foreach (var v in f.Directors.Concat(f.Writers)) S("people").Add(v);
                if (f.Collection.Length > 0) S("set").Add(f.Collection);
            }
            catch { /* skip unreadable NFOs */ }
        }

        IReadOnlyList<string> L(string key) => sets.TryGetValue(key, out var s) ? s.ToList() : Array.Empty<string>();
        return new LibraryVocabulary
        {
            Genres = L("genre"),
            Tags = L("tag"),
            Studios = L("studio"),
            Countries = L("country"),
            People = L("people"),
            Collections = L("set"),
        };
    }

    /// <summary>Values that contain what's typed (starts-with matches first), minus ones already chosen.</summary>
    public static List<string> Match(IReadOnlyList<string> pool, string typed, IEnumerable<string>? exclude = null, int max = 12)
    {
        typed = typed.Trim();
        if (typed.Length == 0) return new List<string>();
        var skip = new HashSet<string>(exclude ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return pool
            .Where(v => !skip.Contains(v) && v.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }
}
