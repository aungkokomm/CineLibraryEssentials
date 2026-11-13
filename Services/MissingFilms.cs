using System.Globalization;

namespace CineLibraryEssentials.Services;

/// <summary>
/// Which films of a TMDb collection are in Step 3's list, which are missing,
/// and which aren't out yet (those never count as missing).
/// </summary>
public static class MissingFilms
{
    public enum State { InList, Missing, NotOutYet }

    public record Film(int TmdbId, string Title, int Year, State State);

    /// <summary>The collection's films oldest first (ones with no date last), each with its state.</summary>
    public static List<Film> Compare(IEnumerable<TmdbApiClient.CollectionPart> parts, IReadOnlySet<string> inList, DateTime today)
        => parts
            .Select(p =>
            {
                DateTime? released = DateTime.TryParseExact(p.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                                            DateTimeStyles.None, out var d) ? d : null;
                var state = inList.Contains(p.TmdbId.ToString(CultureInfo.InvariantCulture)) ? State.InList
                    : released is { } r && r <= today ? State.Missing
                    : State.NotOutYet;
                return new Film(p.TmdbId, p.Title, released?.Year ?? 0, state);
            })
            .OrderBy(f => f.Year == 0 ? int.MaxValue : f.Year)
            .ThenBy(f => f.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
