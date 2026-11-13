using CineLibraryEssentials.Services;
using Xunit;

namespace CineLibraryEssentials.Tests;

/// <summary>A collection's films: in the list, missing, or not out yet (CLE #4, jo-el414's collections).</summary>
public class MissingFilmsTests
{
    private static readonly TmdbApiClient.CollectionPart[] Indy =
    {
        new(89, "Indiana Jones and the Last Crusade", "1989-05-24"),
        new(85, "Raiders of the Lost Ark", "1981-06-12"),
        new(87, "Indiana Jones and the Temple of Doom", "1984-05-23"),
        new(999, "Indiana Jones 6", "2030-01-01"),
        new(998, "Untitled", ""),
    };

    [Fact]
    public void MarksEachFilmAndSortsThemOldestFirst()
    {
        var films = MissingFilms.Compare(Indy, new HashSet<string> { "85", "89" }, new DateTime(2026, 11, 12));

        Assert.Equal(new[] { 85, 87, 89, 999, 998 }, films.Select(f => f.TmdbId));
        Assert.Equal(new[]
        {
            MissingFilms.State.InList, MissingFilms.State.Missing, MissingFilms.State.InList,
            MissingFilms.State.NotOutYet, MissingFilms.State.NotOutYet,
        }, films.Select(f => f.State));
        Assert.Equal(1981, films[0].Year);
        Assert.Equal(0, films[^1].Year);
    }

    [Fact]
    public void AFilmNotOutYetThatYouHaveCountsAsInTheList()
        => Assert.Equal(MissingFilms.State.InList,
            MissingFilms.Compare(Indy, new HashSet<string> { "999" }, new DateTime(2026, 11, 12)).Single(f => f.TmdbId == 999).State);
}
