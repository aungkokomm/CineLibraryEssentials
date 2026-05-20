namespace CineLibraryEssentials.Utilities;

/// <summary>
/// Finds companion files for a video (subtitles, with language / forced / cc
/// suffixes) and produces destination names that preserve the suffix while
/// switching the base to the renamed video's name.
///
/// Used by both Step 1 (in-place rename) and Step 2 (organize-to-folder) so
/// the matching logic can't drift between them.
///
/// Handled cases:
///   Movie.srt            -> Renamed.srt
///   Movie.en.srt         -> Renamed.en.srt
///   Movie.en.forced.srt  -> Renamed.en.forced.srt
///   Movie.eng.cc.srt     -> Renamed.eng.cc.srt
///   Movie.sup            -> Renamed.sup           (HDR / Blu-ray PGS subs)
/// </summary>
public static class CompanionFileHelper
{
    /// <summary>
    /// File extensions we treat as subtitle/companion files. Anything else in
    /// the source folder is left alone.
    /// </summary>
    private static readonly HashSet<string> SubtitleExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".srt", ".sub", ".ass", ".ssa", ".vtt", ".idx", ".sup"
        };

    public record CompanionMove(string SourcePath, string DestinationFileName);

    /// <summary>
    /// Given the path of the source video and the desired renamed video filename,
    /// returns every subtitle / companion file in the same folder that "belongs"
    /// to this video, along with the filename each one should land at.
    /// </summary>
    public static IEnumerable<CompanionMove> Find(string sourceVideoPath, string renamedVideoFile)
    {
        var sourceDir = Path.GetDirectoryName(sourceVideoPath);
        if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            yield break;

        var videoBase = Path.GetFileNameWithoutExtension(sourceVideoPath);
        var renamedBase = Path.GetFileNameWithoutExtension(renamedVideoFile);
        if (string.IsNullOrEmpty(videoBase) || string.IsNullOrEmpty(renamedBase))
            yield break;

        string[] entries;
        try { entries = Directory.GetFiles(sourceDir); }
        catch { yield break; }

        foreach (var path in entries)
        {
            var fileName = Path.GetFileName(path);
            var ext = Path.GetExtension(fileName);
            if (!SubtitleExtensions.Contains(ext)) continue;

            var nameNoExt = Path.GetFileNameWithoutExtension(fileName);

            // Three match shapes:
            //   exact:     nameNoExt == videoBase            -> suffix = ""
            //   suffix:    nameNoExt startsWith videoBase+'.' -> suffix = remainder ("." prefixed)
            //   else:      not a companion of this video
            string suffix;
            if (string.Equals(nameNoExt, videoBase, StringComparison.OrdinalIgnoreCase))
            {
                suffix = string.Empty;
            }
            else if (nameNoExt.Length > videoBase.Length
                     && nameNoExt.StartsWith(videoBase, StringComparison.OrdinalIgnoreCase)
                     && nameNoExt[videoBase.Length] == '.')
            {
                // e.g. "Movie.en.forced" -> suffix = ".en.forced"
                suffix = nameNoExt[videoBase.Length..];
            }
            else
            {
                continue;
            }

            yield return new CompanionMove(path, $"{renamedBase}{suffix}{ext}");
        }
    }
}
