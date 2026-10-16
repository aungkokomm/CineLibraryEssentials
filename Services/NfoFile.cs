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
}
