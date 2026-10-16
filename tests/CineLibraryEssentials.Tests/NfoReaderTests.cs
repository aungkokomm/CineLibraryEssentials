using CineLibraryEssentials.Services;
using Xunit;

namespace CineLibraryEssentials.Tests;

public class NfoReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cle_reader_" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Folder(string name, params (string File, string Content)[] files)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (var (f, content) in files) File.WriteAllText(Path.Combine(dir, f), content);
        return dir;
    }

    [Fact]
    public void ReadsEveryRatingSourceMainOneFirst()
    {
        // Main (default) rating written second, as another tool might
        var dir = Folder("Inception (2010)", ("Inception (2010).nfo", """
            <movie><title>Inception</title>
              <ratings>
                <rating name="themoviedb" max="10"><value>8.4</value><votes>38000</votes></rating>
                <rating name="imdb" default="true" max="10"><value>8.8</value><votes>2600000</votes></rating>
              </ratings>
            </movie>
            """));

        var d = new NfoReaderService().ReadFromFolder(dir)!;

        Assert.Equal(new[] { "imdb", "themoviedb" }, d.Ratings.Select(r => r.Name));
        Assert.Equal("IMDb ★ 8.8 · 2.6M votes", NfoReaderService.MovieDetail.RatingLabel(d.Ratings[0]));
        Assert.Equal("TMDb ★ 8.4 · 38K votes", NfoReaderService.MovieDetail.RatingLabel(d.Ratings[1]));
    }

    [Theory]
    [InlineData("metacritic", 7.4, 42, "Metacritic ★ 7.4 · 42 votes")]
    [InlineData("", 6.5, 0, "★ 6.5")]
    public void LabelsOtherAndUnnamedSources(string name, double value, int votes, string expected)
        => Assert.Equal(expected, NfoReaderService.MovieDetail.RatingLabel(new RatingsXml.Entry(name, value, votes, false)));

    [Fact]
    public void ShowFolderReadsTvshowNfoAndPlainPoster()
    {
        // The episode NFO sorts first alphabetically and must not be picked
        var dir = Folder("Dark",
            ("Dark - S01E01.nfo", "<episodedetails><title>Secrets</title></episodedetails>"),
            ("tvshow.nfo", "<tvshow><title>Dark</title><year>2017</year></tvshow>"),
            ("poster.jpg", "x"));

        var d = new NfoReaderService().ReadFromFolder(dir)!;

        Assert.Equal("Dark", d.Title);
        Assert.Equal(Path.Combine(dir, "poster.jpg"), d.PosterPath);
    }
}
