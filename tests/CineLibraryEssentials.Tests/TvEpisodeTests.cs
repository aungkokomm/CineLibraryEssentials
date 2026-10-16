using System.Text.Json;
using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using Xunit;

namespace CineLibraryEssentials.Tests;

[Collection("Parser")]   // the scanner runs the filename parser, which has shared settings
public class TvEpisodeScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cle_tv_" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Touch(string relative, string content = "")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData("Show - S01E01E02 - Pilot.mkv", 1, 2)]
    [InlineData("Show.S01E01-E02.720p.mkv", 1, 2)]
    [InlineData("Show S02E05-06.mkv", 5, 6)]
    [InlineData("Show.S01E01E02E03.mkv", 1, 3)]
    [InlineData("Show - S01E01 - Pilot.mkv", 1, 1)]
    [InlineData("Show.S01E01-1080p.mkv", 1, 1)]          // a resolution, not episode 108
    [InlineData("Show - S01E03 - 24 Hours.mkv", 3, 3)]   // a title starting with a number
    public void ReadsDoubleEpisodes(string file, int first, int last)
        => Assert.Equal(last, TvEpisodeScanner.LastEpisodeOf(file, first));

    [Fact]
    public void ScansShowRootAndSeasonFoldersAndSummarizes()
    {
        Touch(@"Season 01\Dark - S01E01 - Secrets.mkv");
        Touch(@"Season 01\Dark - S01E02E03 - Lies.mkv");
        Touch(@"Season 02\Dark - S02E01 - Beginnings.mkv");
        Touch(@"Specials\Dark - S00E01 - Making Of.mkv");
        Touch(@"Season 01\Dark - S01E01 - Secrets.nfo");   // not a video
        Touch(@"poster.jpg");

        var files = TvEpisodeScanner.Scan(_root);

        Assert.Equal(4, files.Count);
        Assert.Equal("Secrets", files.Single(f => f.Season == 1 && f.Episode == 1).EpisodeTitle);
        Assert.Equal(5, TvEpisodeScanner.Covered(files).Count);
        Assert.Equal("TV · 2 seasons · 5 episodes", TvEpisodeScanner.Summary(files));
        Assert.Equal("TV · 2 seasons · 5 episodes · 3 missing", TvEpisodeScanner.Summary(files, missing: 3));
    }

    [Fact]
    public void SummaryUsesSingularForOne()
    {
        Touch(@"Mini - S01E01.mkv");
        Assert.Equal("TV · 1 season · 1 episode", TvEpisodeScanner.Summary(TvEpisodeScanner.Scan(_root)));
    }

    [Fact]
    public void EpisodeListReadsTitlesThumbsAndSorts()
    {
        Touch(@"Season 01\Dark - S01E02.mkv");
        Touch(@"Season 01\Dark - S01E02.nfo", "<episodedetails><title>Lies</title><aired>2017-12-01</aired></episodedetails>");
        Touch(@"Season 01\Dark - S01E02-thumb.jpg", "x");
        Touch(@"Season 01\Dark - S01E01.mkv");             // no NFO yet: falls back to the filename

        var eps = NfoReaderService.ReadEpisodes(_root);

        Assert.Equal(new[] { 1, 2 }, eps.Select(e => e.Episode));
        Assert.Equal("Dark - S01E01", eps[0].Title);
        Assert.Null(eps[0].ThumbPath);
        Assert.Equal("Lies", eps[1].Title);
        Assert.Equal("2017-12-01", eps[1].Aired);
        Assert.EndsWith("Dark - S01E02-thumb.jpg", eps[1].ThumbPath);
    }
}

public class MissingEpisodesTests
{
    private static List<TvEpisodeMetadata> Season(int s, int count)
        => Enumerable.Range(1, count).Select(e => new TvEpisodeMetadata
        { SeasonNumber = s, EpisodeNumber = e, Name = $"Ep {e}", AirDate = $"2020-01-{e:D2}" }).ToList();

    private static TvShowMetadata Show(int lastSeason, int lastEpisode, params (int Season, int Count)[] seasons) => new()
    {
        LastEpisodeToAir = new TvEpisodeMetadata { SeasonNumber = lastSeason, EpisodeNumber = lastEpisode },
        Seasons = seasons.Select(s => new TvSeasonInfo { SeasonNumber = s.Season, EpisodeCount = s.Count }).ToList(),
    };

    [Fact]
    public void ListsAiredEpisodesNotOnDiskAndWholeMissingSeasons()
    {
        // On disk: S01 except E03, S02 E01-E02 only. TMDb: 3 full seasons + specials;
        // season 3 is airing, last aired S03E04 of 8.
        var local = new HashSet<(int, int)> { (1, 1), (1, 2), (1, 4), (2, 1), (2, 2) };
        var show = Show(3, 4, (0, 5), (1, 4), (2, 3), (3, 8));
        var lists = new Dictionary<int, List<TvEpisodeMetadata>> { [1] = Season(1, 4), [2] = Season(2, 3) };

        var gaps = MissingEpisodes.Find(local, show, lists);

        Assert.Equal(new[] { (1, 3, 1), (2, 3, 1), (3, 0, 4) }, gaps.Select(g => (g.Season, g.Episode, g.Count)));
        Assert.Equal("Ep 3", gaps[0].Title);
        Assert.Equal(6, MissingEpisodes.Total(gaps));   // specials never count
    }

    [Fact]
    public void UnairedEpisodesAreNotMissing()
    {
        // Season 1 has 10 episodes announced but only 6 aired; the library has all 6
        var local = Enumerable.Range(1, 6).Select(e => (1, e)).ToHashSet();
        var gaps = MissingEpisodes.Find(local, Show(1, 6, (1, 10), (2, 8)),
            new Dictionary<int, List<TvEpisodeMetadata>> { [1] = Season(1, 10) });
        Assert.Empty(gaps);   // season 2 hasn't started either
    }

    [Fact]
    public void NothingReportedWhenTmdbHasNoAiredEpisode()
    {
        var show = new TvShowMetadata { Seasons = { new TvSeasonInfo { SeasonNumber = 1, EpisodeCount = 8 } } };
        Assert.Empty(MissingEpisodes.Find(new HashSet<(int, int)>(), show, new Dictionary<int, List<TvEpisodeMetadata>>()));
    }

    [Fact]
    public void AFailedSeasonDownloadReportsNothingForThatSeason()
    {
        // GetTvSeasonAsync returns an empty list when TMDb can't be reached
        var gaps = MissingEpisodes.Find(new HashSet<(int, int)> { (1, 1) }, Show(1, 10, (1, 10)),
            new Dictionary<int, List<TvEpisodeMetadata>> { [1] = new() });
        Assert.Empty(gaps);
    }
}

public class TmdbJsonTests
{
    [Fact]
    public void NullNumbersReadAsZeroInsteadOfFailing()
    {
        // TMDb's shape for an episode that hasn't aired yet
        var ep = JsonSerializer.Deserialize<TvEpisodeMetadata>(
            """{"id":1,"name":"TBA","episode_number":9,"season_number":2,"runtime":null,"vote_average":null,"air_date":null}""",
            TmdbJson.Options)!;
        Assert.Equal(9, ep.EpisodeNumber);
        Assert.Equal(0, ep.Runtime);
        Assert.Equal(0, ep.Rating);
    }

    [Fact]
    public void ShowDetailsIncludeSeasonsAndLastAiredEpisode()
    {
        var show = JsonSerializer.Deserialize<TvShowMetadata>("""
            {"id":70523,"name":"Dark","vote_average":8.4,
             "last_episode_to_air":{"season_number":3,"episode_number":8,"runtime":null},
             "seasons":[{"season_number":0,"episode_count":2,"air_date":null},
                        {"season_number":1,"episode_count":10,"air_date":"2017-12-01"}]}
            """, TmdbJson.Options)!;
        Assert.Equal((3, 8), (show.LastEpisodeToAir!.SeasonNumber, show.LastEpisodeToAir.EpisodeNumber));
        Assert.Equal(new[] { 0, 1 }, show.Seasons.Select(s => s.SeasonNumber));
        Assert.Equal(10, show.Seasons[1].EpisodeCount);
    }
}
