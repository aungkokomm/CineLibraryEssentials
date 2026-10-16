using CineLibraryEssentials.Services;
using Xunit;

namespace CineLibraryEssentials.Tests;

public class EpisodeFileRenamerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cle_rename_" + Guid.NewGuid().ToString("N")[..8]);

    public EpisodeFileRenamerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Touch(string name, string content = "x")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string[] Names() => Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n).ToArray()!;

    [Fact]
    public void RenamesVideoWithItsSidecarsOnly()
    {
        var video = Touch("Dark - S01E01.mkv");
        Touch("Dark - S01E01.nfo");
        Touch("Dark - S01E01-thumb.jpg");
        Touch("Dark - S01E01.srt");
        Touch("Dark - S01E01.en.srt");
        Touch("Dark - S01E01.pt-BR.forced.srt");
        // Not sidecars: another episode file and a double episode that share the prefix
        Touch("Dark - S01E01 - Other Cut.mkv");
        Touch("Dark - S01E01-E02.mkv");

        var result = EpisodeFileRenamer.RenameWithSidecars(video, "Dark - S01E01 - Secrets");

        Assert.Equal(Path.Combine(_dir, "Dark - S01E01 - Secrets.mkv"), result);
        Assert.Equal(new[]
        {
            "Dark - S01E01 - Other Cut.mkv",
            "Dark - S01E01 - Secrets-thumb.jpg",
            "Dark - S01E01 - Secrets.en.srt",
            "Dark - S01E01 - Secrets.mkv",
            "Dark - S01E01 - Secrets.nfo",
            "Dark - S01E01 - Secrets.pt-BR.forced.srt",
            "Dark - S01E01 - Secrets.srt",
            "Dark - S01E01-E02.mkv",
        }, Names());
    }

    [Fact]
    public void NeverOverwritesAnExistingFile()
    {
        var video = Touch("Dark - S01E01.mkv", "mine");
        Touch("Dark - S01E01.en.srt");
        Touch("Dark - S01E01 - Secrets.en.srt", "someone else's");

        Assert.Null(EpisodeFileRenamer.RenameWithSidecars(video, "Dark - S01E01 - Secrets"));
        Assert.Equal("mine", File.ReadAllText(video));
        Assert.Equal("someone else's", File.ReadAllText(Path.Combine(_dir, "Dark - S01E01 - Secrets.en.srt")));
        Assert.True(File.Exists(Path.Combine(_dir, "Dark - S01E01.en.srt")));
    }

    [Fact]
    public void UndoPutsBackEverythingIncludingFilesWrittenAfterTheRename()
    {
        var video = Touch("Dark - S01E01.mkv");
        Touch("Dark - S01E01.en.srt");
        var renamed = EpisodeFileRenamer.RenameWithSidecars(video, "Dark - S01E01 - Secrets")!;
        // The scrape writes the NFO and thumbnail under the new name
        Touch("Dark - S01E01 - Secrets.nfo");
        Touch("Dark - S01E01 - Secrets-thumb.jpg");

        var restored = EpisodeFileRenamer.Undo(new[] { new EpisodeFileRenamer.Rename(video, renamed) });

        Assert.Equal(1, restored);
        Assert.Equal(new[] { "Dark - S01E01-thumb.jpg", "Dark - S01E01.en.srt", "Dark - S01E01.mkv", "Dark - S01E01.nfo" }, Names());
    }

    [Theory]
    [InlineData("Chapter One: The Vanishing of Will Byers", "Chapter One - The Vanishing of Will Byers")]
    [InlineData("What?", "What")]
    [InlineData("  Ozymandias  ", "Ozymandias")]
    [InlineData("To Be Continued...", "To Be Continued")]
    [InlineData("Episode 5", "")]
    [InlineData("TBA", "")]
    [InlineData(null, "")]
    public void CleansTitlesForFileNames(string? title, string expected)
        => Assert.Equal(expected, EpisodeFileRenamer.CleanTitle(title));
}
