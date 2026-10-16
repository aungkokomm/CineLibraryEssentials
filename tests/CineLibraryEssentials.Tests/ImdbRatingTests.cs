using System.Net;
using System.Xml.Linq;
using CineLibraryEssentials.Services;
using Xunit;

namespace CineLibraryEssentials.Tests;

public class ImdbRatingServiceTests
{
    // Fake HTTP: answers every request from a callback and records the URLs.
    private sealed class FakeHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request.RequestUri!.ToString()));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public void ParsesProxyResponse()
    {
        // Real response shape from api.agregarr.org, including an unknown title
        var json = """[{"imdbId":"tt0111161","rating":9.3,"votes":3252447},{"imdbId":"tt0903747","rating":9.5,"votes":2691090},{"imdbId":"tt9999999999","rating":null,"votes":null}]""";
        var parsed = ImdbRatingService.ParseResponse(json).ToDictionary(p => p.ImdbId, p => p.Rating);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(new ImdbRatingService.ImdbRating(9.3, 3252447), parsed["tt0111161"]);
        Assert.Equal(new ImdbRatingService.ImdbRating(9.5, 2691090), parsed["tt0903747"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"error\":\"rate limited\"}")]
    [InlineData("")]
    public void BadResponsesGiveNoRatings(string body)
        => Assert.Empty(ImdbRatingService.ParseResponse(body));

    [Fact]
    public async Task BatchesOneHundredIdsPerRequest()
    {
        var handler = new FakeHandler(_ => Json("[]"));
        var ids = Enumerable.Range(1, 250).Select(i => $"tt{i:D7}");

        await new ImdbRatingService(new HttpClient(handler)).GetRatingsAsync(ids);

        Assert.Equal(3, handler.Urls.Count);
        Assert.Equal(100, handler.Urls[0].Split("id=").Length - 1);
        Assert.Equal(50, handler.Urls[2].Split("id=").Length - 1);
    }

    [Fact]
    public async Task ServiceFailureGivesNoRatingsInsteadOfThrowing()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var r = await new ImdbRatingService(new HttpClient(handler)).GetRatingAsync("tt0111161");
        Assert.Null(r);
    }

    [Fact]
    public async Task InvalidIdsAreNotSent()
    {
        var handler = new FakeHandler(_ => Json("[]"));
        await new ImdbRatingService(new HttpClient(handler)).GetRatingsAsync(new[] { "", "12345", "tt", "nm0000123" });
        Assert.Empty(handler.Urls);
    }
}

public class RatingsXmlTests
{
    [Fact]
    public void ImdbIsDefaultWhenPreferred()
    {
        var block = RatingsXml.Build(7.4, 9120, 7.8, 512000, preferImdb: true)!;
        var entries = RatingsXml.ReadAll(new XElement("movie", block));

        Assert.Equal("imdb", entries[0].Name);
        Assert.True(entries[0].IsDefault);
        Assert.Equal(512000, entries[0].Votes);
        Assert.False(entries[1].IsDefault);
    }

    [Fact]
    public void TmdbIsDefaultWhenImdbNotPreferredOrMissing()
    {
        Assert.Equal("themoviedb", RatingsXml.ReadAll(new XElement("m", RatingsXml.Build(7.4, 1, 7.8, 1, preferImdb: false)))[0].Name);
        var onlyTmdb = RatingsXml.ReadAll(new XElement("m", RatingsXml.Build(7.4, 1, 0, 0, preferImdb: true)));
        Assert.Single(onlyTmdb);
        Assert.True(onlyTmdb[0].IsDefault);
        Assert.Null(RatingsXml.Build(0, 0, 0, 0, true));
    }

    [Fact]
    public void ValuesUseADotOnAnyPcLanguage()
    {
        var saved = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");   // writes 7,4
            var block = RatingsXml.Build(7.4, 1, 0, 0, false)!;
            Assert.Equal("7.4", block.Element("rating")!.Element("value")!.Value);
        }
        finally { Thread.CurrentThread.CurrentCulture = saved; }
    }

    [Fact]
    public void RefreshUpdatesOnlyTheRatings()
    {
        // An NFO as Step 3 writes it, plus a hand-added tag and a third-party rating
        var root = XElement.Parse("""
            <movie>
              <title>Inception</title>
              <ratings>
                <rating name="themoviedb" default="true" max="10"><value>8.4</value><votes>38000</votes></rating>
                <rating name="metacritic" max="10"><value>7.4</value><votes>42</votes></rating>
              </ratings>
              <plot>A thief...</plot>
              <customtag>keep me</customtag>
            </movie>
            """);

        Assert.True(RatingsXml.ApplyImdb(root, 8.8, 2600000, preferImdb: true));

        var entries = RatingsXml.ReadAll(root);
        Assert.Equal(new[] { "imdb", "themoviedb", "metacritic" }, entries.Select(e => e.Name));
        Assert.True(entries[0].IsDefault);
        Assert.Single(entries, e => e.IsDefault);
        Assert.Equal("A thief...", root.Element("plot")!.Value);
        Assert.Equal("keep me", root.Element("customtag")!.Value);
        Assert.Single(root.Elements("ratings"));

        // Second refresh with the same numbers changes nothing
        Assert.False(RatingsXml.ApplyImdb(root, 8.8, 2600000, preferImdb: true));
    }

    [Fact]
    public void RefreshAddsBlockWhenNfoHasNoRatings()
    {
        var root = XElement.Parse("<tvshow><title>Dark</title><plot>p</plot></tvshow>");
        Assert.True(RatingsXml.ApplyImdb(root, 8.7, 500000, preferImdb: true));
        Assert.Equal("ratings", ((XElement)root.Element("title")!.NextNode!).Name.LocalName);
    }

    [Fact]
    public void RefreshKeepsOldSingleRatingAsTmdb()
    {
        // An NFO from before 1.1.10: TMDb's rating in a single <rating>, no <ratings>
        var root = XElement.Parse("<movie><title>Up</title><rating>7.9</rating><votes>19000</votes><tmdbid>14160</tmdbid></movie>");

        Assert.True(RatingsXml.ApplyImdb(root, 8.3, 1100000, preferImdb: true));

        var entries = RatingsXml.ReadAll(root);
        Assert.Equal(new[] { ("imdb", 8.3, true), ("themoviedb", 7.9, false) },
            entries.Select(e => (e.Name, e.Value, e.IsDefault)));
        Assert.Equal(19000, entries[1].Votes);
        Assert.False(RatingsXml.ApplyImdb(root, 8.3, 1100000, preferImdb: true));
    }

    [Fact]
    public void RefreshKeepsOtherSourcesScale()
    {
        var root = XElement.Parse("""
            <movie><title>Up</title><ratings>
              <rating name="themoviedb" default="true" max="10"><value>7.9</value><votes>19000</votes></rating>
              <rating name="tomatometerallcritics" max="100"><value>98</value><votes>290</votes></rating>
            </ratings></movie>
            """);

        Assert.True(RatingsXml.ApplyImdb(root, 8.3, 1100000, preferImdb: true));

        var rt = root.Element("ratings")!.Elements("rating").Single(r => (string?)r.Attribute("name") == "tomatometerallcritics");
        Assert.Equal("100", (string?)rt.Attribute("max"));
        Assert.Equal("98", rt.Element("value")!.Value);
        Assert.Null(rt.Attribute("default"));
    }
}

public class ImdbRefreshServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cle_refresh_" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Folder(string name, params (string File, string Xml)[] files)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (var (f, xml) in files) File.WriteAllText(Path.Combine(dir, f), xml);
        return dir;
    }

    private sealed class Fake(string json) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) }); }
    }

    [Fact]
    public async Task UpdatesMoviesAndShowsInOneRequest()
    {
        var movie = Folder("Inception (2010)", ("Inception (2010).nfo",
            """<?xml version="1.0" encoding="utf-8" standalone="yes"?><movie><title>Inception</title><ratings><rating name="themoviedb" default="true" max="10"><value>8.4</value><votes>38000</votes></rating></ratings><uniqueid type="imdb" default="true">tt1375666</uniqueid><plot>A thief</plot></movie>"""));
        // Episode NFO sorts before tvshow.nfo and must not be touched
        var show = Folder("Dark", ("tvshow.nfo", """<tvshow><title>Dark</title><uniqueid type="imdb">tt5753856</uniqueid></tvshow>"""),
                                  ("Dark - S01E01.nfo", """<episodedetails><title>Secrets</title></episodedetails>"""));
        var noId = Folder("Home Video", ("Home Video.nfo", "<movie><title>Home</title></movie>"));
        var noNfo = Folder("Unscraped (2020)");

        var fake = new Fake("""[{"imdbId":"tt1375666","rating":8.8,"votes":2600000},{"imdbId":"tt5753856","rating":8.7,"votes":500000}]""");
        var svc = new ImdbRefreshService(new ImdbRatingService(new HttpClient(fake)));

        var r = await svc.RefreshAsync(new[] { movie, show, noId, noNfo }, preferImdb: true);

        Assert.Equal(1, fake.Calls);
        Assert.Equal(new ImdbRefreshService.Result(Updated: 2, AlreadyCurrent: 0, NoImdbId: 1, NoRating: 0, NoNfo: 1, Failed: 0), r);

        var movieRoot = XDocument.Load(Path.Combine(movie, "Inception (2010).nfo")).Root!;
        var entries = RatingsXml.ReadAll(movieRoot);
        Assert.Equal("imdb", entries[0].Name);
        Assert.Equal(2600000, entries[0].Votes);
        Assert.Equal("A thief", movieRoot.Element("plot")!.Value);

        Assert.Equal(8.7, RatingsXml.ReadAll(XDocument.Load(Path.Combine(show, "tvshow.nfo")).Root!)[0].Value);
        Assert.Equal("""<episodedetails><title>Secrets</title></episodedetails>""", File.ReadAllText(Path.Combine(show, "Dark - S01E01.nfo")));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));

        // Running again changes nothing
        var again = await svc.RefreshAsync(new[] { movie, show }, preferImdb: true);
        Assert.Equal(0, again.Updated);
        Assert.Equal(2, again.AlreadyCurrent);
    }

    [Fact]
    public async Task ServiceDownLeavesNfosUntouched()
    {
        var xml = """<movie><title>X</title><uniqueid type="imdb">tt0111161</uniqueid></movie>""";
        var movie = Folder("X (1994)", ("X (1994).nfo", xml));
        var fake = new Fake("<html>502 Bad Gateway</html>");

        var r = await new ImdbRefreshService(new ImdbRatingService(new HttpClient(fake))).RefreshAsync(new[] { movie }, true);

        Assert.Equal(1, r.NoRating);
        Assert.Equal(xml, File.ReadAllText(Path.Combine(movie, "X (1994).nfo")));
    }

    [Fact]
    public async Task LooksUpImdbIdForTmdbOnlyNfos()
    {
        // As 1.0 wrote them (<tmdbid>), and a show with only a TMDb uniqueid
        var movie = Folder("Up (2009)", ("Up (2009).nfo",
            "<movie><title>Up</title><rating>7.9</rating><votes>19000</votes><tmdbid>14160</tmdbid></movie>"));
        var show = Folder("Dark", ("tvshow.nfo", """<tvshow><title>Dark</title><uniqueid type="tmdb">70523</uniqueid></tvshow>"""));
        var unknown = Folder("Rare (1950)", ("Rare (1950).nfo", "<movie><title>Rare</title><tmdbid>999</tmdbid></movie>"));

        var asked = new List<(int, bool)>();
        Task<string?> Find(int tmdbId, bool tv, CancellationToken ct)
        {
            asked.Add((tmdbId, tv));
            return Task.FromResult<string?>(tmdbId switch { 14160 => "tt1049413", 70523 => "tt5753856", _ => null });
        }
        var fake = new Fake("""[{"imdbId":"tt1049413","rating":8.3,"votes":1100000},{"imdbId":"tt5753856","rating":8.7,"votes":500000}]""");
        var svc = new ImdbRefreshService(new ImdbRatingService(new HttpClient(fake)), Find);

        var r = await svc.RefreshAsync(new[] { movie, show, unknown }, preferImdb: true);

        Assert.Equal(new[] { (14160, false), (70523, true), (999, false) }, asked);
        Assert.Equal(2, r.Updated);
        Assert.Equal(1, r.NoImdbId);

        var movieRoot = XDocument.Load(Path.Combine(movie, "Up (2009).nfo")).Root!;
        Assert.Equal("tt1049413", movieRoot.Elements("uniqueid").Single(u => (string?)u.Attribute("type") == "imdb").Value);
        Assert.Equal("uniqueid", ((XElement)movieRoot.Element("tmdbid")!.NextNode!).Name.LocalName);
        Assert.Equal(new[] { "imdb", "themoviedb" }, RatingsXml.ReadAll(movieRoot).Select(e => e.Name));
        Assert.Equal("tt5753856", ImdbRefreshService.ReadImdbId(Path.Combine(show, "tvshow.nfo")));

        // The ids are in the NFOs now: no more lookups
        asked.Clear();
        var again = await svc.RefreshAsync(new[] { movie, show }, preferImdb: true);
        Assert.Empty(asked);
        Assert.Equal(2, again.AlreadyCurrent);
    }

    [Fact]
    public async Task WithoutLookupTmdbOnlyNfosCountAsNoId()
    {
        var movie = Folder("Up (2009)", ("Up (2009).nfo", "<movie><title>Up</title><tmdbid>14160</tmdbid></movie>"));
        var fake = new Fake("[]");

        var r = await new ImdbRefreshService(new ImdbRatingService(new HttpClient(fake))).RefreshAsync(new[] { movie }, true);

        Assert.Equal(1, r.NoImdbId);
    }
}
