using CineLibraryEssentials.Models;
using CineLibraryEssentials.Utilities;

namespace CineLibraryEssentials.Services;

public class RenameService
{
    /// <summary>Default Plex/Kodi/Jellyfin format.</summary>
    public const string TemplatePlex = "{Title} ({Year})";
    /// <summary>Year-first sortable format.</summary>
    public const string TemplateYearFirst = "{Year} - {Title}";

    /// <summary>
    /// Builds the Kodi-standard TV episode filename. Format:
    ///   "Show Name - S01E01 - Episode Title"   (when episode title is present)
    ///   "Show Name - S01E01"                   (no title)
    /// Used by both Step 1 (RenameService) and Step 2 (FileToFolderViewModel)
    /// so the format stays consistent across the wizard.
    /// </summary>
    public static string BuildTvFileName(string showName, int season, int episode, string episodeTitle)
    {
        var basePart = $"{showName.Trim()} - S{season:D2}E{episode:D2}";
        if (string.IsNullOrWhiteSpace(episodeTitle)) return basePart;
        return $"{basePart} - {episodeTitle.Trim()}";
    }

    /// <summary>
    /// Wizard mode passed in from the ViewModel — controls how each file is parsed.
    /// "Auto" = per-file detection, "Movies" = force movie path, "TvShows" = force TV path.
    /// </summary>
    public enum Mode { Auto, Movies, TvShows }

    public List<FilePreview> AnalyzeFiles(
        string sourceFolder,
        bool recursive = false,
        string template = TemplatePlex,
        Mode mode = Mode.Auto)
    {
        var previews = new List<FilePreview>();

        if (!Directory.Exists(sourceFolder))
            return previews;

        var searchOption = recursive
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(sourceFolder, "*", searchOption);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating files: {ex.Message}");
            return previews;
        }

        foreach (var file in files)
        {
            if (!FileFormatValidator.IsVideoFile(file))
                continue;

            FileInfo fileInfo;
            try { fileInfo = new FileInfo(file); }
            catch { continue; }

            var fileName = Path.GetFileName(file);
            var extension = Path.GetExtension(file);

            // TV path: if the file matches a TV episode pattern (and mode allows it),
            // format the cleaned name as "Show - S01E01 - Episode Title.ext" (Kodi
            // convention) and capture show/season/episode so Step 2 builds the
            // Show/Season XX/ hierarchy. Mode.Movies disables this entirely so a
            // movie called "Star Wars Episode IV" doesn't get misclassified.
            var tv = mode == Mode.Movies ? null : RegexPatterns.ParseTvEpisode(fileName);
            if (tv != null && !string.IsNullOrEmpty(tv.ShowName))
            {
                var tvCleanedName = PathSanitizer.SanitizeFileName(
                    BuildTvFileName(tv.ShowName, tv.Season, tv.Episode, tv.EpisodeTitle)) + extension;

                previews.Add(new FilePreview
                {
                    OriginalName = fileName,
                    OriginalFilePath = file,
                    FileSizeBytes = fileInfo.Length,
                    CleanedName = tvCleanedName,
                    Confidence = tv.Confidence,
                    IsReviewed = false,
                    IsSelected = true,
                    IsTvEpisode = true,
                    ShowName = tv.ShowName,
                    Season = tv.Season,
                    Episode = tv.Episode,
                    EpisodeTitle = tv.EpisodeTitle,
                });
                continue;
            }

            // Movie path (also: TvShows-mode files that didn't match S/E — they get
            // parsed as movies and shown with a warning that the user should edit
            // the cleaned name manually to a "Show - S01E01" form).
            var parsed = RegexPatterns.ParseFilename(fileName);
            var formatted = ApplyTemplate(parsed.Title, parsed.Year, template);
            var cleanedName = PathSanitizer.SanitizeFileName(formatted) + extension;

            var preview = new FilePreview
            {
                OriginalName = fileName,
                OriginalFilePath = file,
                FileSizeBytes = fileInfo.Length,
                Year = parsed.Year,
                Edition = parsed.Edition,
                CleanedName = cleanedName,
                Confidence = parsed.Confidence,
                IsReviewed = false,
                IsSelected = true,
                IsTvEpisode = false,
            };

            // In TV mode, files without a parsed S/E pattern need attention.
            if (mode == Mode.TvShows)
            {
                preview.HasWarning = true;
                preview.WarningMessage = "No S/E pattern found — edit the name to match \"Show - S01E01\".";
            }

            previews.Add(preview);
        }

        return previews.OrderByDescending(p => p.Confidence).ToList();
    }

    /// <summary>
    /// Applies an output template like "{Title} ({Year})" or "{Year} - {Title}".
    /// Falls back to title-only if year is missing.
    /// </summary>
    public static string ApplyTemplate(string title, int year, string template)
    {
        if (year <= 0)
            return title;

        return template
            .Replace("{Title}", title, StringComparison.OrdinalIgnoreCase)
            .Replace("{Year}", year.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Renames the given files IN PLACE (same folder). Updates each FilePreview's
    /// OriginalName / OriginalFilePath to reflect the new on-disk state.
    /// Companion subtitle files are renamed alongside the video.
    ///
    /// If <paramref name="renameParentFolders"/> is true, also renames the containing
    /// folder for any video that lives in its own subfolder (single-video folder, not
    /// the source root).
    /// </summary>
    public async Task<ProcessingResult> RenameInPlaceAsync(
        IEnumerable<FilePreview> previews,
        bool renameParentFolders = false,
        string? sourceFolder = null,
        bool cleanEmbeddedMetadata = false,
        List<UndoService.RenameRecord>? undoLog = null)
    {
        var previewsList = previews.ToList();
        var result = new ProcessingResult { Success = true };
        var metadataCleaner = cleanEmbeddedMetadata ? new MetadataCleanerService() : null;

        // ---- Pass 1: rename files in their current folder ----
        foreach (var p in previewsList)
        {
            if (string.IsNullOrEmpty(p.OriginalFilePath))
            {
                result.Errors.Add($"{p.OriginalName}: missing source path");
                continue;
            }
            if (!File.Exists(p.OriginalFilePath))
            {
                result.Errors.Add($"{p.OriginalName}: source file not found");
                continue;
            }
            if (string.Equals(p.OriginalName, p.CleanedName, StringComparison.Ordinal))
            {
                // No rename needed, but still clean metadata if user requested it.
                // This is the path that fires when the file is ALREADY named correctly
                // from a previous run — without this, the cleaner silently never runs.
                if (metadataCleaner != null)
                {
                    var metaTitle = Path.GetFileNameWithoutExtension(p.OriginalName);
                    var metaResult = metadataCleaner.Clean(p.OriginalFilePath, metaTitle);
                    if (!metaResult.Success && !string.IsNullOrEmpty(metaResult.Error))
                        result.Errors.Add($"{p.OriginalName}: metadata clean failed: {metaResult.Error}");
                }
                p.IsReviewed = true;
                continue;
            }
            if (string.IsNullOrWhiteSpace(p.CleanedName))
            {
                result.Errors.Add($"{p.OriginalName}: target name is empty");
                continue;
            }
            if (p.CleanedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                result.Errors.Add($"{p.OriginalName}: target has invalid characters");
                continue;
            }

            var dir = Path.GetDirectoryName(p.OriginalFilePath);
            if (string.IsNullOrEmpty(dir))
            {
                result.Errors.Add($"{p.OriginalName}: cannot resolve folder");
                continue;
            }

            var newPath = Path.Combine(dir, p.CleanedName);

            if (File.Exists(newPath))
            {
                result.Errors.Add($"{p.OriginalName}: target already exists");
                continue;
            }

            try
            {
                var oldFilePath = p.OriginalFilePath;
                await Task.Run(() => File.Move(oldFilePath, newPath));
                undoLog?.Add(new UndoService.RenameRecord(oldFilePath, newPath, IsDirectory: false));

                // Rename companion files (subtitles, language-suffixed subs, etc.).
                // CompanionFileHelper handles base-name matches AND .en.srt /
                // .en.forced.srt patterns, preserving the suffix.
                foreach (var companion in Utilities.CompanionFileHelper.Find(oldFilePath, p.CleanedName))
                {
                    var newCompanion = Path.Combine(dir, companion.DestinationFileName);
                    if (File.Exists(newCompanion)) continue; // merge — don't overwrite

                    try
                    {
                        var src = companion.SourcePath;
                        await Task.Run(() => File.Move(src, newCompanion));
                        undoLog?.Add(new UndoService.RenameRecord(src, newCompanion, IsDirectory: false));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Companion rename failed: {companion.SourcePath} -> {newCompanion}: {ex.Message}");
                    }
                }

                // Optionally scrub embedded container metadata (sets Title to clean name,
                // clears Comment/Description/etc.)
                if (metadataCleaner != null)
                {
                    var metaTitle = Path.GetFileNameWithoutExtension(p.CleanedName);
                    var metaResult = metadataCleaner.Clean(newPath, metaTitle);
                    if (!metaResult.Success && !string.IsNullOrEmpty(metaResult.Error))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Metadata clean failed for {newPath}: {metaResult.Error}");
                    }
                }

                // Update the preview to reflect the on-disk state
                p.OriginalFilePath = newPath;
                p.OriginalName = p.CleanedName;
                p.IsReviewed = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"{p.OriginalName}: {ex.Message}");
            }
        }

        // ---- Pass 2: rename containing folder (only if requested) ----
        if (renameParentFolders)
        {
            // Group surviving previews by their parent folder
            var byParent = previewsList
                .Where(p => !string.IsNullOrEmpty(p.OriginalFilePath) && File.Exists(p.OriginalFilePath))
                .GroupBy(p => Path.GetDirectoryName(p.OriginalFilePath) ?? string.Empty)
                .ToList();

            foreach (var group in byParent)
            {
                var parentPath = group.Key;
                if (string.IsNullOrEmpty(parentPath)) continue;

                // Skip the source folder itself — that's the user-picked root, never rename it
                if (!string.IsNullOrEmpty(sourceFolder)
                    && string.Equals(Path.GetFullPath(parentPath).TrimEnd('\\'),
                                     Path.GetFullPath(sourceFolder).TrimEnd('\\'),
                                     StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip multi-video folders — ambiguous which movie to name them after
                if (group.Count() > 1) continue;

                var preview = group.First();
                var grandParent = Path.GetDirectoryName(parentPath);
                if (string.IsNullOrEmpty(grandParent)) continue;

                // New folder name = cleaned filename without extension
                var newFolderName = Path.GetFileNameWithoutExtension(preview.CleanedName);
                if (string.IsNullOrWhiteSpace(newFolderName)) continue;
                newFolderName = PathSanitizer.SanitizeFolderName(newFolderName);

                var oldFolderName = Path.GetFileName(parentPath);
                if (string.Equals(oldFolderName, newFolderName, StringComparison.Ordinal))
                    continue;  // already matches

                var newParentPath = Path.Combine(grandParent, newFolderName);

                if (Directory.Exists(newParentPath))
                {
                    result.Errors.Add($"Folder rename target exists: {newFolderName}");
                    continue;
                }

                try
                {
                    await Task.Run(() => Directory.Move(parentPath, newParentPath));
                    undoLog?.Add(new UndoService.RenameRecord(parentPath, newParentPath, IsDirectory: true));

                    // Update file paths inside the renamed folder
                    foreach (var inner in group)
                    {
                        var innerName = Path.GetFileName(inner.OriginalFilePath);
                        inner.OriginalFilePath = Path.Combine(newParentPath, innerName);
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Errors.Add($"Folder rename failed ({oldFolderName}): {ex.Message}");
                }
            }
        }

        result.Message = result.Errors.Count == 0
            ? "All files renamed successfully"
            : $"{result.Errors.Count} error(s)";

        return result;
    }

    public List<FileOperation> CreateFileOperations(
        string sourceFolder,
        List<FilePreview> previews,
        string outputFolder)
    {
        var operations = new List<FileOperation>();

        foreach (var preview in previews)
        {
            if (!preview.IsSelected) continue;

            // Honour the original (possibly-recursive-scanned) full path
            var sourcePath = !string.IsNullOrEmpty(preview.OriginalFilePath)
                ? preview.OriginalFilePath
                : Path.Combine(sourceFolder, preview.OriginalName);

            if (!File.Exists(sourcePath))
                continue;

            // ---- TV episode path ----
            // Kodi/Plex/Jellyfin all expect TV files at:
            //     <output>/Show Name/Season 01/Show Name S01E01.ext
            // (the "Show Name (Year)" form is also accepted but the simpler form
            // matches what most users expect).
            if (preview.IsTvEpisode && !string.IsNullOrEmpty(preview.ShowName) && preview.Season > 0)
            {
                var showFolder = PathSanitizer.SanitizeFolderName(preview.ShowName);
                var seasonFolder = $"Season {preview.Season:D2}";
                var destFolder = Path.Combine(outputFolder, showFolder, seasonFolder);

                operations.Add(new FileOperation
                {
                    OriginalFilePath = sourcePath,
                    OriginalFileName = preview.OriginalName,
                    CleanedTitle = preview.ShowName,
                    Year = 0,
                    Confidence = preview.Confidence,
                    DestinationFolder = destFolder,
                    FinalFileName = preview.CleanedName,
                    ShowName = preview.ShowName,
                    Season = preview.Season,
                    Episode = preview.Episode,
                    EpisodeTitle = preview.EpisodeTitle,
                });
                continue;
            }

            // ---- Movie path (unchanged) ----
            // Folder name = filename without extension (sanitized)
            var folderName = Path.GetFileNameWithoutExtension(preview.CleanedName);
            if (string.IsNullOrWhiteSpace(folderName))
                folderName = Path.GetFileNameWithoutExtension(preview.OriginalName);
            folderName = PathSanitizer.SanitizeFolderName(folderName);

            // Re-extract title/year from the (possibly user-edited) cleaned name
            var parsed = RegexPatterns.ParseFilename(preview.CleanedName);

            var destinationFolder = Path.Combine(outputFolder, folderName);

            operations.Add(new FileOperation
            {
                OriginalFilePath = sourcePath,
                OriginalFileName = preview.OriginalName,
                CleanedTitle = parsed.Title,
                Year = parsed.Year,
                Edition = string.IsNullOrEmpty(preview.Edition) ? parsed.Edition : preview.Edition,
                Confidence = preview.Confidence,
                DestinationFolder = destinationFolder,
                FinalFileName = preview.CleanedName
            });
        }

        return operations;
    }
}
