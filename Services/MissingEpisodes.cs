using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

/// <summary>
/// Compares the episodes on disk with TMDb's episode list and reports the ones
/// that have aired but aren't in the library. Specials (season 0) are ignored,
/// and so is anything after TMDb's last aired episode.
/// </summary>
public static class MissingEpisodes
{
    /// <summary>
    /// One missing episode, or a whole missing season when Episode is 0
    /// (Count is then how many of its episodes have aired).
    /// </summary>
    public record Gap(int Season, int Episode, string Title, string AirDate, int Count = 1);

    /// <param name="local">Episodes on disk (see <see cref="TvEpisodeScanner.Covered"/>).</param>
    /// <param name="show">TMDb show details (season list + last aired episode).</param>
    /// <param name="seasonEpisodes">TMDb episode lists already fetched, by season number.</param>
    public static List<Gap> Find(
        ISet<(int Season, int Episode)> local,
        TvShowMetadata show,
        IReadOnlyDictionary<int, List<TvEpisodeMetadata>> seasonEpisodes)
    {
        var gaps = new List<Gap>();
        var last = show.LastEpisodeToAir;
        if (last == null) return gaps;   // nothing has aired yet, or TMDb doesn't know

        bool Aired(int s, int e) => s < last.SeasonNumber || (s == last.SeasonNumber && e <= last.EpisodeNumber);
        var localSeasons = local.Select(x => x.Season).ToHashSet();

        // Seasons the library has: each aired episode that isn't on disk
        foreach (var (season, episodes) in seasonEpisodes)
        {
            if (season <= 0 || !localSeasons.Contains(season)) continue;
            foreach (var ep in episodes)
            {
                if (Aired(season, ep.EpisodeNumber) && !local.Contains((season, ep.EpisodeNumber)))
                    gaps.Add(new Gap(season, ep.EpisodeNumber, ep.Name, ep.AirDate ?? string.Empty));
            }
        }

        // Seasons the library doesn't have at all: one line per season
        foreach (var s in show.Seasons)
        {
            if (s.SeasonNumber <= 0 || localSeasons.Contains(s.SeasonNumber)) continue;
            var aired = s.SeasonNumber < last.SeasonNumber ? s.EpisodeCount
                      : s.SeasonNumber == last.SeasonNumber ? last.EpisodeNumber
                      : 0;
            if (aired > 0)
                gaps.Add(new Gap(s.SeasonNumber, 0, $"Season {s.SeasonNumber}", s.AirDate ?? string.Empty, aired));
        }

        return gaps.OrderBy(g => g.Season).ThenBy(g => g.Episode).ToList();
    }

    /// <summary>How many episodes the gaps add up to.</summary>
    public static int Total(IEnumerable<Gap> gaps) => gaps.Sum(g => g.Count);

    /// <summary>
    /// Fetches what <see cref="Find"/> needs from TMDb (show details plus the
    /// episode lists of the seasons on disk). Null when TMDb can't be reached.
    /// </summary>
    public static async Task<List<Gap>?> CheckAsync(TmdbApiClient tmdb, int tvId, ISet<(int Season, int Episode)> local)
    {
        var show = await tmdb.GetTvDetailsAsync(tvId);
        if (show == null) return null;

        var seasonEpisodes = new Dictionary<int, List<TvEpisodeMetadata>>();
        foreach (var season in local.Select(x => x.Season).Where(s => s > 0).Distinct())
            seasonEpisodes[season] = await tmdb.GetTvSeasonAsync(tvId, season);

        return Find(local, show, seasonEpisodes);
    }
}
