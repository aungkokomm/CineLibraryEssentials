using System.Text.RegularExpressions;

namespace CineLibraryEssentials.Services;

/// <summary>
/// Finds a TV show's episode files and reads their season/episode numbers from
/// the filenames. Shared by the TV scraper, the Step 3 card line
/// ("TV · 3 seasons · 24 episodes") and the details window's episode list.
/// </summary>
public static class TvEpisodeScanner
{
    /// <summary>
    /// One episode file. LastEpisode is above Episode for a double episode like
    /// S01E01E02. EpisodeTitle is the title in the file name ("" when none).
    /// </summary>
    public record EpisodeFile(string VideoFile, int Season, int Episode, int LastEpisode, string ShowName, string EpisodeTitle);

    // The extra numbers of a double episode: "S01E01E02", "S01E01-E02", "S01E01-02".
    // (?!\d) stops "S01E01-1080p" from reading as episodes 1 to 108.
    private static readonly Regex MultiEpisodePattern = new(
        @"S\d{1,3}E\d{1,3}(?:(?:[\-_]?E|-)(?<x>\d{1,3})(?!\d))+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Every video directly in the folder or one level down (a flat season
    /// folder, or Season XX/ subfolders) whose name has a TV episode pattern.
    /// </summary>
    public static List<EpisodeFile> Scan(string rootFolder)
    {
        var list = new List<EpisodeFile>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(rootFolder))
                TryAdd(f, list);

            foreach (var sub in Directory.EnumerateDirectories(rootFolder))
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(sub))
                        TryAdd(f, list);
                }
                catch { /* permissions */ }
            }
        }
        catch { /* permissions */ }
        return list;
    }

    private static void TryAdd(string filePath, List<EpisodeFile> list)
    {
        if (!Utilities.FileFormatValidator.IsVideoFile(filePath)) return;
        var name = Path.GetFileName(filePath);
        var parse = Utilities.RegexPatterns.ParseTvEpisode(name);
        if (parse == null) return;
        list.Add(new EpisodeFile(filePath, parse.Season, parse.Episode,
            LastEpisodeOf(name, parse.Episode), parse.ShowName, parse.EpisodeTitle));
    }

    /// <summary>The last episode a file holds: 2 for "S01E01E02", else the first episode.</summary>
    public static int LastEpisodeOf(string fileName, int firstEpisode)
    {
        var m = MultiEpisodePattern.Match(fileName);
        if (!m.Success) return firstEpisode;
        var last = m.Groups["x"].Captures
            .Select(c => int.Parse(c.Value))
            .DefaultIfEmpty(firstEpisode)
            .Max();
        // Only a short run after the first episode is believable as a double episode
        return last > firstEpisode && last - firstEpisode <= 5 ? last : firstEpisode;
    }

    /// <summary>Every (season, episode) the files cover, with double episodes counted in full.</summary>
    public static HashSet<(int Season, int Episode)> Covered(IEnumerable<EpisodeFile> files)
    {
        var set = new HashSet<(int, int)>();
        foreach (var f in files)
            for (int e = f.Episode; e <= f.LastEpisode; e++)
                set.Add((f.Season, e));
        return set;
    }

    /// <summary>
    /// Card line such as "TV · 3 seasons · 24 episodes · 2 missing". Specials
    /// (season 0) count as episodes but not as a season.
    /// </summary>
    public static string Summary(IReadOnlyCollection<EpisodeFile> files, int missing = 0)
    {
        var covered = Covered(files);
        var seasons = covered.Select(c => c.Season).Where(s => s > 0).Distinct().Count();
        var parts = new List<string> { "TV" };
        if (seasons > 0) parts.Add(Plural(seasons, "season"));
        if (covered.Count > 0) parts.Add(Plural(covered.Count, "episode"));
        if (missing > 0) parts.Add($"{missing} missing");
        return string.Join(" · ", parts);
    }

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
}
