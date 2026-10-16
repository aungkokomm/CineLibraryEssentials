using CineLibraryEssentials.Utilities;
using Xunit;

namespace CineLibraryEssentials.Tests;

/// <summary>
/// Filename parser tests built from real download names, including the ones
/// reported on GitHub. Every parser fix should add its failing names here so
/// the behavior can't silently regress.
/// </summary>
[Collection("Parser")]
public class TvParserTests
{
    [Theory]
    // GitHub issue #3 (jo-el414): release groups were left behind as "episode titles"
    [InlineData("Lost.in.Space.2018.S03E01.WEBRip.x264-ION10.mp4", "Lost in Space", 3, 1, "")]
    [InlineData("Stargate.Universe.S01E03.BDRip.x265-ION265.mp4", "Stargate Universe", 1, 3, "")]
    [InlineData("Poker.Face.2023.S02E01.480p.x264-mSD[EZTVx.to].mkv", "Poker Face", 2, 1, "")]
    // Same bug family: any group after a codec tag leaked into the title
    [InlineData("Breaking.Bad.S01E03.And.the.Bags.in.the.River.1080p.BluRay.x265-RARBG.mkv", "Breaking Bad", 1, 3, "And the Bags in the River")]
    [InlineData("Billions.S02E05.Currency.1080p-RARBG.mkv", "Billions", 2, 5, "Currency")]
    [InlineData("Show.S01E01.1080p.AMZN.WEB-DL.DDP2.0.H.264-NTb.mkv", "Show", 1, 1, "")]
    [InlineData("Show.S01E01.PROPER.720p.HDTV.x264-KILLERS.mkv", "Show", 1, 1, "")]
    // Real episode titles must survive
    [InlineData("Billions - S02E05 - Currency.mkv", "Billions", 2, 5, "Currency")]
    [InlineData("Sherlock.S01E01.A.Study.in.Pink.720p.HDTV.x264.mkv", "Sherlock", 1, 1, "A Study in Pink")]
    [InlineData("Doctor.Who.2005.S01E01.Rose.mkv", "Doctor Who", 1, 1, "Rose")]
    [InlineData("Show.S01E02.Title.Here-GRP[ettv].mkv", "Show", 1, 2, "Title Here")]
    [InlineData("Show.S01E04.Spider-Man.720p.HDTV.mkv", "Show", 1, 4, "Spider-Man")]
    [InlineData("Dark - S03E01 - Deja-vu.mkv", "Dark", 3, 1, "Deja-Vu")]
    // No title at all
    [InlineData("Show.S01E01-GRP.mkv", "Show", 1, 1, "")]
    [InlineData("Show.S01E01.mkv", "Show", 1, 1, "")]
    public void ParsesEpisode(string file, string show, int season, int episode, string title)
    {
        var r = RegexPatterns.ParseTvEpisode(file);
        Assert.NotNull(r);
        Assert.Equal(show, r!.ShowName);
        Assert.Equal(season, r.Season);
        Assert.Equal(episode, r.Episode);
        Assert.Equal(title, r.EpisodeTitle);
    }

    [Theory]
    [InlineData("The.Matrix.1999.1080p.BluRay.mkv")]
    [InlineData("Movie.Title.2019.1080p.WEBRip.x264-RARBG.mp4")]
    public void MoviesAreNotEpisodes(string file)
        => Assert.Null(RegexPatterns.ParseTvEpisode(file));
}

[Collection("Parser")]
public class MovieParserTests
{
    [Theory]
    // Hyphenated titles: the last word was eaten as a "release group"
    [InlineData("Spider-Man (2002).mkv", "Spider-Man", 2002)]
    [InlineData("Spider-Man.2002.1080p.BluRay.x264-SPARKS.mkv", "Spider-Man", 2002)]
    [InlineData("X-Men.2000.720p.BluRay.x264.mkv", "X-Men", 2000)]
    [InlineData("Monster-in-Law (2005) ENG Sub, UThura, American, Comedy, IMDB 5.6.mkv", "Monster-in-Law", 2005)]
    [InlineData("Ant-Man.and.the.Wasp.2018.1080p.WEB-DL.mkv", "Ant-Man and the Wasp", 2018)]
    [InlineData("Dhurandhar-The Revenge (2026).mkv", "Dhurandhar-The Revenge", 2026)]
    [InlineData("Install Hackintosh MacOS Tahoe _ Sequoia on ANY PC_Laptop (Easy Guide).mp4", "Install Hackintosh Macos Tahoe Sequoia on Any Pc Laptop", 0)]
    // Real-world names from the README screenshots
    [InlineData("The Dry (2020) Australia, Crime, Drama, Mystery, IMDB 6.9, RT 90, Eric Bana.mkv", "The Dry", 2020)]
    [InlineData("Raising Arizona (1987) ENG Sub, UThura, American, Comedy, Crime, IMDB 7.3.mkv", "Raising Arizona", 1987)]
    [InlineData("Obsession (2026) MA WEBRip Hindi + English DV+HDR10 2160p 10bit HEVC DDP 5.1 MSub CineVood.mkv", "Obsession", 2026)]
    [InlineData("UnTouch.The.Kerala.Story.2.2026.1080p.WEB-HDRip.Hindi.DDP5.1.MULTi.x264.ESub-india4Movies.Diy.mkv", "The Kerala Story 2", 2026)]
    // Year-like numbers inside the title
    [InlineData("Blade.Runner.2049.2017.2160p.UHD.BluRay.x265-TERMINAL.mkv", "Blade Runner 2049", 2017)]
    [InlineData("Kill.Bill.Vol.1.2003.1080p.BluRay.mkv", "Kill Bill Vol 1", 2003)]
    [InlineData("Movie.Title.2019.1080p.WEBRip.x264-RARBG.mp4", "Movie Title", 2019)]
    // No year: the title ends at the first technical tag
    [InlineData("Some.Movie.1080p.WEB-DL.x264-GRP.mkv", "Some Movie", 0)]
    public void ParsesTitleAndYear(string file, string title, int year)
    {
        var r = RegexPatterns.ParseFilename(file);
        Assert.Equal(title, r.Title);
        Assert.Equal(year, r.Year);
    }

    [Fact]
    public void YearInsideTitleIsNotTheReleaseYear()
    {
        var r = RegexPatterns.ParseFilename("2001.A.Space.Odyssey.1968.1080p.BluRay.x264.mkv");
        Assert.Equal(1968, r.Year);
        Assert.Equal("2001 a space odyssey", r.Title.ToLowerInvariant());
    }

    [Fact]
    public void DetectsEdition()
    {
        var r = RegexPatterns.ParseFilename("The.Matrix.1999.Directors.Cut.1080p.mkv");
        Assert.Equal("The Matrix", r.Title);
        Assert.Equal("Director's Cut", r.Edition);
    }
}
