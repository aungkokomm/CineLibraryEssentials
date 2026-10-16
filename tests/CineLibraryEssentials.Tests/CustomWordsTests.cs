using CineLibraryEssentials.Utilities;
using Xunit;

namespace CineLibraryEssentials.Tests;

/// <summary>
/// The "Words to always remove" list from Settings. It's global parser state,
/// so these share the "Parser" collection (no parallel runs) and reset after.
/// </summary>
[Collection("Parser")]
public class CustomWordsTests : IDisposable
{
    public void Dispose() => RegexPatterns.SetCustomRemoveWords(Array.Empty<string>());

    [Fact]
    public void RemovesWordsFromEpisodeTitle()
    {
        Assert.Equal("Episode Name Ion10", RegexPatterns.ParseTvEpisode("Show.S01E01.Episode.Name.ION10.mkv")!.EpisodeTitle);

        RegexPatterns.SetCustomRemoveWords(new[] { "ion10" });   // case-insensitive
        Assert.Equal("Episode Name", RegexPatterns.ParseTvEpisode("Show.S01E01.Episode.Name.ION10.mkv")!.EpisodeTitle);
    }

    [Fact]
    public void RemovesWordsFromMovieTitle()
    {
        RegexPatterns.SetCustomRemoveWords(new[] { "HDHub4u", "ESub" });
        var r = RegexPatterns.ParseFilename("Some Movie ESub HDHub4u.mkv");
        Assert.Equal("Some Movie", r.Title);
    }

    [Fact]
    public void OnlyWholeWordsAreRemoved()
    {
        // "Man" as its own word goes; inside "Manhattan" it stays
        RegexPatterns.SetCustomRemoveWords(new[] { "Man" });
        Assert.Equal("Manhattan", RegexPatterns.ParseFilename("Manhattan Man (1979).mkv").Title);
    }

    [Fact]
    public void AcceptsWordsTypedWithLeadingDash()
    {
        RegexPatterns.SetCustomRemoveWords(new[] { "-CineVood" });
        Assert.Equal("Episode Name", RegexPatterns.ParseTvEpisode("Show.S01E01.Episode.Name-CineVood.mkv")!.EpisodeTitle);
    }
}
