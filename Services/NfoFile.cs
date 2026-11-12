using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CineLibraryEssentials.Services;

/// <summary>Loads and safely rewrites existing NFO files.</summary>
public static class NfoFile
{
    public static XDocument Load(string path) => XDocument.Load(path);

    /// <summary>
    /// Saves as UTF-8 (no BOM), indented, like Step 3 writes them. Writes to a
    /// temporary file first and then swaps it in, so a crash or full disk can't
    /// leave a half-written NFO behind.
    /// </summary>
    public static void Save(XDocument doc, string path)
    {
        var tmp = path + ".tmp";
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
        };
        using (var writer = XmlWriter.Create(tmp, settings))
            doc.Save(writer);
        File.Move(tmp, path, overwrite: true);
    }

    private static readonly System.Text.RegularExpressions.Regex Declaration =
        new(@"^\s*<\?xml[^>]*\?>", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Every &lt;episodedetails&gt; in an episode NFO: one, or one per episode for a
    /// double episode, which Kodi writes as several root elements in one file.
    /// </summary>
    public static List<XElement> LoadEpisodes(string path) => ParseEpisodes(File.ReadAllText(path));

    /// <summary>The root elements of NFO text: one, or several for a double episode.</summary>
    public static List<XElement> ParseEpisodes(string xml)
    {
        var body = Declaration.Replace(xml, string.Empty);
        return XElement.Parse("<parts>" + body + "</parts>").Elements().ToList();
    }

    /// <summary>Saves episode details back, side by side for a double episode as Kodi reads them.</summary>
    public static void SaveEpisodes(IReadOnlyList<XElement> parts, string path)
    {
        if (parts.Count == 1)
        {
            Save(new XDocument(new XDeclaration("1.0", "utf-8", "yes"), new XElement(parts[0])), path);
            return;
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, EpisodesXml(parts), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>The text of an episode NFO with one or more &lt;episodedetails&gt;.</summary>
    public static string EpisodesXml(IEnumerable<XElement> parts)
        => "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>" + Environment.NewLine
           + string.Join(Environment.NewLine, parts.Select(p => p.ToString()));

    private static readonly string[] UserFields = { "userrating", "playcount", "lastplayed", "watched", "user_note" };

    /// <summary>
    /// A rescrape builds the .nfo afresh from TMDb, so the user's own fields
    /// (rating, play count, last played, watched, note) are taken over from the
    /// old file at <paramref name="oldPath"/>. Episodes are matched by number, for
    /// a double episode's file. Returns <paramref name="newXml"/> as it is when
    /// there's nothing to keep.
    /// </summary>
    public static string KeepUserFields(string newXml, string? oldPath)
    {
        if (oldPath == null || !File.Exists(oldPath)) return newXml;
        List<XElement> oldRoots, newRoots;
        try
        {
            oldRoots = LoadEpisodes(oldPath);
            newRoots = ParseEpisodes(newXml);
        }
        catch
        {
            return newXml;   // an unreadable old file has nothing to keep
        }

        bool kept = false;
        foreach (var root in newRoots)
        {
            var old = oldRoots.FirstOrDefault(o => o.Name == root.Name
                                                   && (string?)o.Element("episode") == (string?)root.Element("episode"));
            if (old == null) continue;
            foreach (var name in UserFields)
            {
                var value = old.Element(name)?.Value.Trim();
                if (string.IsNullOrEmpty(value) || value == "0") continue;
                var target = root.Element(name);
                if (target == null) root.Add(new XElement(name, value));
                else target.Value = value;
                kept = true;
            }
        }
        if (!kept) return newXml;
        return newRoots.Count == 1 ? newRoots[0].ToString() : EpisodesXml(newRoots);
    }
}
