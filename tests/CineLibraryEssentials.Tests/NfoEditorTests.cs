using System.Xml.Linq;
using CineLibraryEssentials.Services;
using Xunit;

namespace CineLibraryEssentials.Tests;

public class NfoEditorTests : IDisposable
{
    // A movie NFO as Step 3 writes it, plus things the editor must never touch
    private const string MovieNfo = """
        <?xml version="1.0" encoding="utf-8" standalone="yes"?>
        <movie>
          <title>Inception</title>
          <originaltitle>Inception</originaltitle>
          <ratings>
            <rating name="imdb" default="true" max="10"><value>8.8</value><votes>2600000</votes></rating>
          </ratings>
          <year>2010</year>
          <plot>A thief who steals corporate secrets...</plot>
          <tagline>Your mind is the scene of the crime.</tagline>
          <mpaa>PG-13</mpaa>
          <uniqueid type="tmdb" default="true">27205</uniqueid>
          <genre>Action</genre>
          <genre>Science Fiction</genre>
          <country>United States of America</country>
          <set><name>Nolan Films</name><overview>Some overview</overview></set>
          <credits>Christopher Nolan</credits>
          <director>Christopher Nolan</director>
          <studio>Warner Bros. Pictures</studio>
          <studio>Lions Gate Films, Inc.</studio>
          <actor><name>Leonardo DiCaprio</name><role>Cobb</role></actor>
          <customtag from="other-tool">keep me</customtag>
        </movie>
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cle_edit_" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static XElement Movie() => XElement.Parse(MovieNfo.Substring(MovieNfo.IndexOf("<movie>", StringComparison.Ordinal)));

    [Fact]
    public void ReadsTheEditableFields()
    {
        var f = NfoEditor.Read(Movie());
        Assert.Equal("Inception", f.Title);
        Assert.Equal(2010, f.Year);
        Assert.Equal(new[] { "Action", "Science Fiction" }, f.Genres);
        Assert.Equal(new[] { "Warner Bros. Pictures", "Lions Gate Films, Inc." }, f.Studios);
        Assert.Equal(new[] { "Christopher Nolan" }, f.Writers);
        Assert.Equal("PG-13", f.AgeRating);
        Assert.Equal("Nolan Films", f.Collection);
        Assert.Equal(0, f.UserRating);
    }

    [Fact]
    public void SavingWithoutChangesLeavesTheNfoIdentical()
    {
        var root = Movie();
        var before = root.ToString();
        var f = NfoEditor.Read(root);

        Assert.False(NfoEditor.Apply(root, f, f, lockData: false));
        Assert.Equal(before, root.ToString());
    }

    [Fact]
    public void ChangesOnlyEditedFieldsAndKeepsEverythingElse()
    {
        var root = Movie();
        var f = NfoEditor.Read(root);
        var edited = f with
        {
            Title = "Inception (Director's Notes)",
            Genres = NfoEditor.SplitList("Thriller, Action , thriller"),
            UserRating = 9,
            Tagline = "",
        };

        Assert.True(NfoEditor.Apply(root, f, edited, lockData: true));

        Assert.Equal("Inception (Director's Notes)", root.Element("title")!.Value);
        Assert.Equal(new[] { "Thriller", "Action" }, root.Elements("genre").Select(g => g.Value));
        Assert.Equal("country", ((XElement)root.Elements("genre").Last().NextNode!).Name.LocalName);   // genres stay in place
        Assert.Equal("9", root.Element("userrating")!.Value);
        Assert.Null(root.Element("tagline"));
        Assert.Equal("true", root.Element("lockdata")!.Value);
        // Untouched: the studio with a comma, ratings, actors, a stranger's tag
        Assert.Equal(new[] { "Warner Bros. Pictures", "Lions Gate Films, Inc." }, root.Elements("studio").Select(s => s.Value));
        Assert.Equal("2600000", root.Element("ratings")!.Element("rating")!.Element("votes")!.Value);
        Assert.Equal("Cobb", root.Element("actor")!.Element("role")!.Value);
        Assert.Equal("keep me", root.Element("customtag")!.Value);
        Assert.Equal("other-tool", (string?)root.Element("customtag")!.Attribute("from"));
    }

    [Fact]
    public void NewCollectionDropsTheOldOverviewAndEmptyRemovesIt()
    {
        var root = Movie();
        var f = NfoEditor.Read(root);

        NfoEditor.Apply(root, f, f with { Collection = "Dream Heists" }, lockData: false);
        Assert.Equal("Dream Heists", root.Element("set")!.Element("name")!.Value);
        Assert.Null(root.Element("set")!.Element("overview"));

        var g = NfoEditor.Read(root);
        NfoEditor.Apply(root, g, g with { Collection = "" }, lockData: false);
        Assert.Null(root.Element("set"));
    }

    [Fact]
    public void ReadsOldStyleCollectionAndUnlocks()
    {
        var root = XElement.Parse("<movie><title>X</title><set>Old Style</set><lockdata>true</lockdata></movie>");
        var f = NfoEditor.Read(root);
        Assert.Equal("Old Style", f.Collection);
        Assert.True(NfoEditor.IsLocked(root));

        Assert.True(NfoEditor.Apply(root, f, f, lockData: false));
        Assert.Null(root.Element("lockdata"));
    }

    [Fact]
    public void ShowTitleFollowsTheTitle()
    {
        var root = XElement.Parse("<tvshow><title>Dark</title><showtitle>Dark</showtitle></tvshow>");
        var f = NfoEditor.Read(root);
        NfoEditor.Apply(root, f, f with { Title = "Dark (2017)" }, lockData: false);
        Assert.Equal("Dark (2017)", root.Element("showtitle")!.Value);
    }

    [Fact]
    public void LockInfoForAutomaticScrapes()
    {
        Directory.CreateDirectory(_dir);
        var nfo = Path.Combine(_dir, "Inception (2010).nfo");
        File.WriteAllText(nfo, MovieNfo);
        Assert.Equal((false, (int?)null), NfoEditor.LockInfo(_dir));

        // Round trip through the editor + NfoFile.Save, as the dialog does
        var doc = NfoFile.Load(nfo);
        var f = NfoEditor.Read(doc.Root!);
        NfoEditor.Apply(doc.Root!, f, f with { Plot = "Edited plot" }, lockData: true);
        NfoFile.Save(doc, nfo);

        Assert.Equal((true, (int?)27205), NfoEditor.LockInfo(_dir));
        var reread = NfoEditor.Read(NfoFile.Load(nfo).Root!);
        Assert.Equal("Edited plot", reread.Plot);
        Assert.Equal(f.Studios, reread.Studios);
    }
}
