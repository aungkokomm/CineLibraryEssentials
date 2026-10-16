using System.Xml.Linq;
using CineLibraryEssentials.Services;
using Xunit;

namespace CineLibraryEssentials.Tests;

public class NfoEditorRichTests
{
    private static XElement Movie() => XElement.Parse("""
        <movie>
          <title>Inception</title>
          <outline>Short</outline>
          <plot>Long</plot>
          <runtime>148</runtime>
          <id>tt1375666</id>
          <uniqueid type="imdb" default="true">tt1375666</uniqueid>
          <uniqueid type="tmdb">27205</uniqueid>
          <country>United States of America</country>
          <premiered>2010-07-15</premiered>
          <released>2010-07-15</released>
          <trailer>plugin://trailer</trailer>
          <actor><name>Leonardo DiCaprio</name><role>Cobb</role><order>0</order><thumb>.actors/Leonardo_DiCaprio.jpg</thumb><tmdbid>6193</tmdbid></actor>
          <actor><name>Elliot Page</name><role>Ariadne</role><order>1</order><thumb>.actors/Elliot_Page.jpg</thumb></actor>
          <actor><name>Tom Hardy</name><role>Eames</role><order>2</order></actor>
          <fileinfo><streamdetails/></fileinfo>
        </movie>
        """);

    [Fact]
    public void ReadsTheNewFields()
    {
        var f = NfoEditor.Read(Movie());
        Assert.Equal(148, f.Runtime);
        Assert.Equal("Short", f.Outline);
        Assert.Equal("2010-07-15", f.Premiered);
        Assert.Equal("plugin://trailer", f.Trailer);
        Assert.Equal("tt1375666", f.ImdbId);
        Assert.Equal("27205", f.TmdbId);
        Assert.Equal(new[] { "United States of America" }, f.Countries);
        Assert.Equal(3, f.Cast.Count);
        Assert.Equal(new NfoEditor.Actor("Elliot Page", "Ariadne", ".actors/Elliot_Page.jpg"), f.Cast[1]);
    }

    [Fact]
    public void CastEditsKeepPositionOrderAndExtraTags()
    {
        var root = Movie();
        var f = NfoEditor.Read(root);
        // Move Tom Hardy first, change Leo's role, drop Elliot, add someone new
        var cast = new List<NfoEditor.Actor>
        {
            f.Cast[2],
            f.Cast[0] with { Role = "Dom Cobb" },
            new("Cillian Murphy", "Fischer", ".actors/Cillian_Murphy.jpg"),
        };

        Assert.True(NfoEditor.Apply(root, f, f with { Cast = cast }, lockData: false));

        var actors = root.Elements("actor").ToList();
        Assert.Equal(new[] { "Tom Hardy", "Leonardo DiCaprio", "Cillian Murphy" }, actors.Select(a => a.Element("name")!.Value));
        Assert.Equal(new[] { "0", "1", "2" }, actors.Select(a => a.Element("order")!.Value));
        Assert.Equal("Dom Cobb", actors[1].Element("role")!.Value);
        Assert.Equal("6193", actors[1].Element("tmdbid")!.Value);                  // kept from the old element
        Assert.Null(actors[0].Element("thumb"));                                   // still no photo
        Assert.Equal("fileinfo", ((XElement)actors[2].NextNode!).Name.LocalName); // same place in the file
    }

    [Fact]
    public void IdAndDateChangesKeepTheirTwinsInStep()
    {
        var root = Movie();
        var f = NfoEditor.Read(root);
        NfoEditor.Apply(root, f, f with { ImdbId = "tt0000001", Premiered = "2010-07-16" }, lockData: false);

        Assert.Equal("tt0000001", root.Elements("uniqueid").First(u => (string?)u.Attribute("type") == "imdb").Value);
        Assert.Equal("true", (string?)root.Elements("uniqueid").First().Attribute("default"));
        Assert.Equal("tt0000001", root.Element("id")!.Value);
        Assert.Equal("2010-07-16", root.Element("released")!.Value);

        var g = NfoEditor.Read(root);
        NfoEditor.Apply(root, g, g with { TmdbId = "" }, lockData: false);
        Assert.DoesNotContain(root.Elements("uniqueid"), u => (string?)u.Attribute("type") == "tmdb");
    }

    [Fact]
    public void CountsChangedFields()
    {
        var f = NfoEditor.Read(Movie());
        Assert.Equal(0, NfoEditor.CountChanges(f, f));
        Assert.Equal(3, NfoEditor.CountChanges(f, f with { Title = "X", Runtime = 90, Tags = new[] { "4K" } }));
    }

    [Fact]
    public void BulkChangeAddsAndRemovesPerItem()
    {
        var f = new NfoEditor.Fields { Genres = new[] { "Action", "Drama" }, Tags = new[] { "Watched" }, Collection = "Old" };
        var change = new NfoEditor.BulkChange
        {
            AddGenres = new[] { "Thriller", "action" },
            RemoveGenres = new[] { "drama" },
            AddTags = new[] { "4K" },
            Collection = "Nolan",
        };

        var after = NfoEditor.ApplyBulk(f, change);

        Assert.Equal(new[] { "Action", "Thriller" }, after.Genres);
        Assert.Equal(new[] { "Watched", "4K" }, after.Tags);
        Assert.Equal("Nolan", after.Collection);
        Assert.Equal("", NfoEditor.ApplyBulk(f, new NfoEditor.BulkChange { Collection = "" }).Collection);
        Assert.Same(f.Genres, NfoEditor.ApplyBulk(f, new NfoEditor.BulkChange()).Genres);   // untouched
    }

    [Fact]
    public void EpisodeEditsOnlyTouchChangedFields()
    {
        var root = XElement.Parse("<episodedetails><title>Secrets</title><aired>2017-12-01</aired><plot>p</plot><thumb>x.jpg</thumb></episodedetails>");
        var e = NfoEditor.ReadEpisode(root);
        Assert.False(NfoEditor.ApplyEpisode(root, e, e));
        Assert.True(NfoEditor.ApplyEpisode(root, e, e with { Title = "Secrets (Part 1)" }));
        Assert.Equal("Secrets (Part 1)", root.Element("title")!.Value);
        Assert.Equal("x.jpg", root.Element("thumb")!.Value);
    }

    [Fact]
    public void ParsesTmdbImageLists()
    {
        var json = """
            {"id":27205,
             "backdrops":[{"file_path":"/a.jpg","width":3840,"height":2160,"iso_639_1":null,"vote_average":5.2},
                          {"file_path":"/b.jpg","width":1920,"height":1080,"iso_639_1":"en","vote_average":5.6}],
             "logos":[{"file_path":"/logo.svg","width":500,"height":100,"iso_639_1":"en","vote_average":5.0},
                      {"file_path":"/logo.png","width":800,"height":200,"iso_639_1":"en","vote_average":4.0}],
             "posters":[{"file_path":"/p.jpg","width":2000,"height":3000,"iso_639_1":"fr","vote_average":null}]}
            """;
        var (posters, backdrops, logos) = TmdbApiClient.ParseImages(json);

        Assert.Equal(new[] { "/b.jpg", "/a.jpg" }, backdrops.Select(b => b.FilePath));   // best voted first
        Assert.Null(backdrops[1].Language);
        Assert.Equal("/logo.png", Assert.Single(logos).FilePath);                       // SVG skipped
        Assert.Equal("fr", posters[0].Language);
        Assert.Equal(0, posters[0].Vote);
    }
}

public class NfoBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cle_backup_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _savedRoot = NfoBackup.Root;

    public NfoBackupTests()
    {
        Directory.CreateDirectory(_dir);
        NfoBackup.Root = Path.Combine(_dir, "store");
    }

    public void Dispose()
    {
        NfoBackup.Root = _savedRoot;
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void RestorePutsBackChangedFilesAndRemovesNewOnes()
    {
        var nfo = Path.Combine(_dir, "Movie.nfo");
        var poster = Path.Combine(_dir, "Movie-poster.jpg");
        var logo = Path.Combine(_dir, "Movie-clearlogo.png");
        File.WriteAllText(nfo, "old nfo");
        File.WriteAllText(poster, "old poster");

        var set = NfoBackup.Begin(nfo);
        set.Add(nfo);
        set.Add(poster);
        set.Add(logo);                      // doesn't exist yet: the save creates it
        File.WriteAllText(nfo, "new nfo");
        File.WriteAllText(poster, "new poster");
        File.WriteAllText(logo, "new logo");

        Assert.NotNull(NfoBackup.BackupTime(nfo));
        Assert.Equal(3, NfoBackup.Restore(nfo));

        Assert.Equal("old nfo", File.ReadAllText(nfo));
        Assert.Equal("old poster", File.ReadAllText(poster));
        Assert.False(File.Exists(logo));
        Assert.Null(NfoBackup.BackupTime(nfo));   // used up
    }

    [Fact]
    public void BulkEditChangesEachNfoAndCanBeRestoredPerTitle()
    {
        string Folder(string name, string xml)
        {
            var d = Path.Combine(_dir, name);
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, name + ".nfo"), xml);
            return d;
        }
        var a = Folder("A (2010)", "<movie><title>A</title><genre>Action</genre></movie>");
        var b = Folder("B (2012)", "<movie><title>B</title><genre>Thriller</genre><set><name>Nolan</name></set></movie>");
        var empty = Path.Combine(_dir, "No NFO");
        Directory.CreateDirectory(empty);

        var r = BulkNfoEdit.Apply(new[] { a, b, empty },
            new NfoEditor.BulkChange { Collection = "Nolan", AddGenres = new[] { "Thriller" }, Lock = true });

        Assert.Equal(new BulkNfoEdit.Result(Updated: 2, Unchanged: 0, NoNfo: 1, Failed: 0), r);
        var fa = NfoEditor.Read(NfoFile.Load(Path.Combine(a, "A (2010).nfo")).Root!);
        Assert.Equal(new[] { "Action", "Thriller" }, fa.Genres);
        Assert.Equal("Nolan", fa.Collection);
        Assert.Equal((true, (int?)null), NfoEditor.LockInfo(a));

        // Same change again: nothing to do
        Assert.Equal(2, BulkNfoEdit.Apply(new[] { a, b }, new NfoEditor.BulkChange { Collection = "Nolan" }).Unchanged);

        NfoBackup.Restore(Path.Combine(a, "A (2010).nfo"));
        Assert.Equal("<movie><title>A</title><genre>Action</genre></movie>", File.ReadAllText(Path.Combine(a, "A (2010).nfo")));
    }

    [Fact]
    public void NewSaveReplacesTheOlderBackup()
    {
        var nfo = Path.Combine(_dir, "Show.nfo");
        File.WriteAllText(nfo, "v1");
        NfoBackup.Begin(nfo).Add(nfo);
        File.WriteAllText(nfo, "v2");
        NfoBackup.Begin(nfo).Add(nfo);
        File.WriteAllText(nfo, "v3");

        NfoBackup.Restore(nfo);
        Assert.Equal("v2", File.ReadAllText(nfo));
        Assert.Equal(0, NfoBackup.Restore(nfo));
    }
}

public class LibraryVocabularyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cle_vocab_" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Folder(string name, string nfoName, string xml)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, nfoName), xml);
        return dir;
    }

    [Fact]
    public void CollectsValuesAcrossTheLibraryAndMatchesWhatsTyped()
    {
        var a = Folder("A (2010)", "A (2010).nfo", "<movie><genre>Science Fiction</genre><genre>Action</genre><studio>Syncopy</studio><set><name>Nolan</name></set><director>Christopher Nolan</director></movie>");
        var b = Folder("Dark", "tvshow.nfo", "<tvshow><genre>science fiction</genre><genre>Drama</genre><tag>German</tag></tvshow>");
        var broken = Folder("Broken", "Broken.nfo", "<movie><genre>");

        var v = LibraryVocabulary.Collect(new[] { a, b, broken });

        Assert.Equal(new[] { "Action", "Drama", "Science Fiction" }, v.Genres);
        Assert.Equal(new[] { "Nolan" }, v.Collections);
        Assert.Equal(new[] { "Christopher Nolan" }, v.People);
        Assert.Equal(new[] { "German" }, v.Tags);

        // Starts-with first, then contains; already chosen values left out
        Assert.Equal(new[] { "Science Fiction" }, LibraryVocabulary.Match(v.Genres, "sci"));
        Assert.Equal(new[] { "Action", "Science Fiction" }, LibraryVocabulary.Match(v.Genres, "ion", exclude: new[] { "Drama" }));
        Assert.Empty(LibraryVocabulary.Match(v.Genres, "  "));
    }
}
