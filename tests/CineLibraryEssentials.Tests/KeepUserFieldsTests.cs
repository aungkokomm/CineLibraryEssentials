using System.Xml.Linq;
using CineLibraryEssentials.Services;
using Xunit;

namespace CineLibraryEssentials.Tests;

/// <summary>
/// A rescrape builds the .nfo afresh from TMDb, but keeps what only the user
/// knows: their rating, plays, watched, last played and note.
/// </summary>
public class KeepUserFieldsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cle_keep_" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Write(string content)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "old.nfo");
        File.WriteAllText(path, content);
        return path;
    }

    private const string Fresh = """
        <movie><title>Inception</title><userrating>0</userrating><plot>New plot</plot><playcount>0</playcount></movie>
        """;

    [Fact]
    public void KeepsTheUsersOwnFields()
    {
        var old = Write("""
            <movie><title>Old</title><userrating>9</userrating><playcount>2</playcount><lastplayed>2026-11-01</lastplayed>
              <watched>true</watched><user_note>Watch with Dad</user_note><plot>Old plot</plot></movie>
            """);

        var root = XElement.Parse(NfoFile.KeepUserFields(Fresh, old));

        Assert.Equal("Inception", root.Element("title")!.Value);
        Assert.Equal("New plot", root.Element("plot")!.Value);
        Assert.Equal("9", root.Element("userrating")!.Value);
        Assert.Equal("2", root.Element("playcount")!.Value);
        Assert.Equal("2026-11-01", root.Element("lastplayed")!.Value);
        Assert.Equal("true", root.Element("watched")!.Value);
        Assert.Equal("Watch with Dad", root.Element("user_note")!.Value);
    }

    [Fact]
    public void LeavesTheNewNfoAsItIsWhenThereIsNothingToKeep()
    {
        Assert.Equal(Fresh, NfoFile.KeepUserFields(Fresh, null));
        Assert.Equal(Fresh, NfoFile.KeepUserFields(Fresh, Path.Combine(_root, "missing.nfo")));
        Assert.Equal(Fresh, NfoFile.KeepUserFields(Fresh, Write("<movie><userrating>0</userrating><playcount>0</playcount></movie>")));
        Assert.Equal(Fresh, NfoFile.KeepUserFields(Fresh, Write("not xml <")));
    }

    [Fact]
    public void MatchesADoubleEpisodesPartsByNumber()
    {
        var old = Write("""
            <episodedetails><season>3</season><episode>1</episode><playcount>1</playcount></episodedetails>
            <episodedetails><season>3</season><episode>2</episode><playcount>4</playcount></episodedetails>
            """);
        var fresh = NfoFile.EpisodesXml(new[]
        {
            XElement.Parse("<episodedetails><title>Part One</title><season>3</season><episode>1</episode></episodedetails>"),
            XElement.Parse("<episodedetails><title>Part Two</title><season>3</season><episode>2</episode></episodedetails>"),
        });

        var parts = NfoFile.ParseEpisodes(NfoFile.KeepUserFields(fresh, old));

        Assert.Equal(new[] { "1", "4" }, parts.Select(p => p.Element("playcount")!.Value));
        Assert.Equal(new[] { "Part One", "Part Two" }, parts.Select(p => p.Element("title")!.Value));
    }
}
