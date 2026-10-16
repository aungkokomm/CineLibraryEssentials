namespace CineLibraryEssentials.Services;

/// <summary>Finds the NFO that describes a whole movie or TV-show folder.</summary>
public static class NfoLocator
{
    /// <summary>
    /// tvshow.nfo when present, otherwise the first NFO that isn't a single
    /// episode's &lt;episodedetails&gt;. Episode NFOs ("Show - S01E01.nfo") sort
    /// before "tvshow.nfo", so never just take the first *.nfo. Null if none.
    /// </summary>
    public static string? FindMainNfo(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return null;

        var tvShowNfo = Path.Combine(folderPath, "tvshow.nfo");
        if (File.Exists(tvShowNfo)) return tvShowNfo;

        return Directory.EnumerateFiles(folderPath, "*.nfo").FirstOrDefault(f => !IsEpisodeNfo(f));
    }

    /// <summary>True if the NFO's root element is &lt;episodedetails&gt; (a single TV episode).</summary>
    public static bool IsEpisodeNfo(string nfoPath)
    {
        try
        {
            using var reader = System.Xml.XmlReader.Create(nfoPath);
            return reader.MoveToContent() == System.Xml.XmlNodeType.Element
                   && reader.LocalName.Equals("episodedetails", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
