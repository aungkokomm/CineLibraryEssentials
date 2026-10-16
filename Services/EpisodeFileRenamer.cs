using System.Text.RegularExpressions;

namespace CineLibraryEssentials.Services;

/// <summary>
/// Renames an episode video together with the files that share its name
/// (subtitles like "Name.en.srt", "Name.nfo", "Name-thumb.jpg"). Used by the
/// opt-in "Add episode titles to file names" and its Undo.
/// </summary>
public static class EpisodeFileRenamer
{
    /// <summary>A renamed video (its sidecars followed it).</summary>
    public record Rename(string From, string To);

    private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".sub", ".idx", ".vtt", ".sup" };

    // TMDb placeholders that add nothing to a file name
    private static readonly Regex PlaceholderTitle = new(@"^(?:Episode\s*\d+|TBA|TBD)$", RegexOptions.IgnoreCase);

    /// <summary>
    /// Renames the video and its sidecars to <paramref name="newBaseName"/> in the
    /// same folder. Never overwrites anything, and puts everything back if a
    /// move fails part-way. Returns the new video path, or null if nothing changed.
    /// </summary>
    public static string? RenameWithSidecars(string videoFile, string newBaseName)
    {
        var dir = Path.GetDirectoryName(videoFile)!;
        var oldBase = Path.GetFileNameWithoutExtension(videoFile);
        if (string.Equals(oldBase, newBaseName, StringComparison.Ordinal) || !File.Exists(videoFile)) return null;

        var moves = new List<(string From, string To)>
        {
            (videoFile, Path.Combine(dir, newBaseName + Path.GetExtension(videoFile))),
        };
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(f);
            if (name.Length <= oldBase.Length || !name.StartsWith(oldBase, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = name[oldBase.Length..];
            if (IsSidecarSuffix(rest)) moves.Add((f, Path.Combine(dir, newBaseName + rest)));
        }

        // Case-only renames point at the same file, which is fine
        if (moves.Any(m => File.Exists(m.To) && !string.Equals(m.From, m.To, StringComparison.OrdinalIgnoreCase)))
            return null;

        var done = new List<(string From, string To)>();
        try
        {
            foreach (var m in moves)
            {
                File.Move(m.From, m.To);
                done.Add(m);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Episode rename failed for {videoFile}: {ex.Message}");
            foreach (var m in Enumerable.Reverse(done))
            {
                try { File.Move(m.To, m.From); } catch { /* best effort */ }
            }
            return null;
        }
        return moves[0].To;
    }

    /// <summary>Puts the original names back. Returns how many videos were restored.</summary>
    public static int Undo(IEnumerable<Rename> renames)
    {
        int restored = 0;
        foreach (var r in renames.Reverse())
        {
            if (RenameWithSidecars(r.To, Path.GetFileNameWithoutExtension(r.From)) != null)
                restored++;
        }
        return restored;
    }

    /// <summary>
    /// An episode title made safe for a file name ("Part 1: Pilot" becomes
    /// "Part 1 - Pilot"). Empty for placeholders like "Episode 5".
    /// </summary>
    public static string CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var t = title.Replace(": ", " - ").Replace(':', '-');
        var invalid = Path.GetInvalidFileNameChars();
        t = new string(t.Where(c => Array.IndexOf(invalid, c) < 0).ToArray());
        t = Regex.Replace(t, @"\s+", " ").Trim().TrimEnd('.', ' ');
        if (t.Length > 100) t = t[..100].TrimEnd('.', ' ');
        return PlaceholderTitle.IsMatch(t) ? string.Empty : t;
    }

    // ".nfo", "-thumb.jpg", or a subtitle with optional short tags: ".srt", ".en.srt", ".pt-BR.forced.srt"
    private static bool IsSidecarSuffix(string rest)
    {
        if (rest.Equals(".nfo", StringComparison.OrdinalIgnoreCase)
            || rest.Equals("-thumb.jpg", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!rest.StartsWith('.')) return false;

        var ext = Path.GetExtension(rest);
        if (!SubtitleExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return false;
        var tags = rest[..^ext.Length].Split('.', StringSplitOptions.RemoveEmptyEntries);
        return tags.All(t => t.Length <= 8 && t.All(c => char.IsLetterOrDigit(c) || c == '-'));
    }
}
