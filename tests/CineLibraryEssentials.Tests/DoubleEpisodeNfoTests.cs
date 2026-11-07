using System.Xml.Linq;
using CineLibraryEssentials.Services;
using Xunit;

namespace CineLibraryEssentials.Tests;

/// <summary>
/// A double episode's .nfo holds one &lt;episodedetails&gt; per episode, side by
/// side, the way Kodi writes it (CineLibraryCS #25, jo-el414's S03E01-02 files).
/// </summary>
public class DoubleEpisodeNfoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cle_double_" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private const string KodiDouble = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
        <episodedetails><title>Part One</title><season>3</season><episode>1</episode><aired>2015-06-12</aired></episodedetails>
        <episodedetails><title>Part Two</title><season>3</season><episode>2</episode><aired>2015-06-12</aired></episodedetails>
        """;

    private string Write(string name, string content)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void LoadsEveryEpisodeOfADoubleEpisode()
    {
        var parts = NfoFile.LoadEpisodes(Write("Defiance - S03E01-E02.nfo", KodiDouble));
        Assert.Equal(new[] { "Part One", "Part Two" }, parts.Select(p => p.Element("title")!.Value));
    }

    [Fact]
    public void LoadsASingleEpisodeAsOnePart()
    {
        var parts = NfoFile.LoadEpisodes(Write("Show - S01E01.nfo", "<episodedetails><title>Pilot</title></episodedetails>"));
        Assert.Equal("Pilot", Assert.Single(parts).Element("title")!.Value);
    }

    [Fact]
    public void SavesBothPartsBackSideBySide()
    {
        var path = Write("Defiance - S03E01-E02.nfo", KodiDouble);
        var parts = NfoFile.LoadEpisodes(path);
        parts[1].Element("title")!.Value = "Part Two (edited)";

        NfoFile.SaveEpisodes(parts, path);

        var text = File.ReadAllText(path);
        Assert.StartsWith("<?xml", text);
        Assert.Equal(new[] { "Part One", "Part Two (edited)" },
            NfoFile.LoadEpisodes(path).Select(p => p.Element("title")!.Value));
    }

    [Fact]
    public void EpisodeListShowsBothTitles()
    {
        var show = Path.Combine(_root, "Defiance");
        var season = Path.Combine(show, "Season 03");
        Directory.CreateDirectory(season);
        File.WriteAllText(Path.Combine(season, "Defiance - S03E01-E02.mkv"), "");
        File.WriteAllText(Path.Combine(season, "Defiance - S03E01-E02.nfo"), KodiDouble);

        var e = Assert.Single(NfoReaderService.ReadEpisodes(show));

        Assert.Equal((1, 2), (e.Episode, e.LastEpisode));
        Assert.Equal("Part One / Part Two", e.Title);
    }
}
