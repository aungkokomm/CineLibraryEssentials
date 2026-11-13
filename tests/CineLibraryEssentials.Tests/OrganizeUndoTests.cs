using CineLibraryEssentials.Models;
using CineLibraryEssentials.Services;
using Xunit;

namespace CineLibraryEssentials.Tests;

/// <summary>Undo after Step 2 puts every file back and takes away the folders it made.</summary>
public class OrganizeUndoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cle_undo_" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Make(string rel)
    {
        var path = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    private FileOperation Movie(string rel, string folder, string name) => new()
    {
        OriginalFilePath = Path.Combine(_root, rel),
        OriginalFileName = Path.GetFileName(rel),
        DestinationFolder = Path.Combine(_root, folder),
        FinalFileName = name,
    };

    [Fact]
    public async Task UndoPutsEverythingBack()
    {
        var movie = Make("Inception (2010).mkv");
        var sub = Make("Inception (2010).srt");
        var yts = Make(@"Interstellar YTS\Interstellar (2014).mp4");
        var subs = Make(@"Interstellar YTS\Subs\English.srt");
        var episode = Make("Breaking Bad - S01E01.mkv");

        var ops = new List<FileOperation>
        {
            Movie("Inception (2010).mkv", "Inception (2010)", "Inception (2010).mkv"),
            Movie(@"Interstellar YTS\Interstellar (2014).mp4", "Interstellar (2014)", "Interstellar (2014).mp4"),
            new()
            {
                OriginalFilePath = episode, OriginalFileName = Path.GetFileName(episode),
                DestinationFolder = Path.Combine(_root, "Breaking Bad (2008)", "Season 01"),
                FinalFileName = "Breaking Bad - S01E01.mkv", ShowName = "Breaking Bad", Season = 1, Episode = 1,
            },
        };
        var result = await new FileToFolderService().OrganizeFilesAsync(ops);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(Path.Combine(_root, "Inception (2010)", "Inception (2010).srt")));
        Assert.True(Directory.Exists(Path.Combine(_root, "Interstellar (2014)", "Subs")));

        var (back, notBack, kept) = FileToFolderService.Undo(result);

        Assert.Equal((5, 0, 0), (back, notBack, kept));   // 3 videos, a subtitle and the Subs folder
        foreach (var f in new[] { movie, sub, yts, subs, episode }) Assert.True(File.Exists(f), f);
        foreach (var d in new[] { "Inception (2010)", "Interstellar (2014)", "Breaking Bad (2008)" })
            Assert.False(Directory.Exists(Path.Combine(_root, d)), d);
    }

    [Fact]
    public async Task UndoKeepsAFolderThatGotOtherFiles()
    {
        Make("Heat (1995).mkv");
        var result = await new FileToFolderService().OrganizeFilesAsync(new() { Movie("Heat (1995).mkv", "Heat (1995)", "Heat (1995).mkv") });
        Make(@"Heat (1995)\Heat (1995).nfo");   // Step 3 scraped it since

        var (back, notBack, kept) = FileToFolderService.Undo(result);

        Assert.Equal((1, 0, 1), (back, notBack, kept));
        Assert.True(File.Exists(Path.Combine(_root, "Heat (1995).mkv")));
        Assert.True(File.Exists(Path.Combine(_root, "Heat (1995)", "Heat (1995).nfo")));
    }

    [Fact]
    public async Task UndoLeavesAFileThatMovedOnSince()
    {
        Make("Heat (1995).mkv");
        var result = await new FileToFolderService().OrganizeFilesAsync(new() { Movie("Heat (1995).mkv", "Heat (1995)", "Heat (1995).mkv") });
        File.Move(Path.Combine(_root, "Heat (1995)", "Heat (1995).mkv"), Path.Combine(_root, "Heat (1995)", "Heat.mkv"));

        var (back, notBack, _) = FileToFolderService.Undo(result);

        Assert.Equal((0, 1), (back, notBack));
        Assert.True(File.Exists(Path.Combine(_root, "Heat (1995)", "Heat.mkv")));
    }
}
