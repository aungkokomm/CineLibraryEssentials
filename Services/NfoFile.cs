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
    public static List<XElement> LoadEpisodes(string path)
    {
        var body = Declaration.Replace(File.ReadAllText(path), string.Empty);
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
}
