using CineLibraryEssentials.Models;
using CineLibraryEssentials.Utilities;

namespace CineLibraryEssentials.Services;

public class RenameService
{
    /// <summary>Default Plex/Kodi/Jellyfin format.</summary>
    public const string TemplatePlex = "{Title} ({Year})";
    /// <summary>Year-first sortable format.</summary>
    public const string TemplateYearFirst = "{Year} - {Title}";

    public List<FilePreview> AnalyzeFiles(
        string sourceFolder,
        bool recursive = false,
        string template = TemplatePlex)
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
            var parsed = RegexPatterns.ParseFilename(fileName);
            var extension = Path.GetExtension(file);

            var formatted = ApplyTemplate(parsed.Title, parsed.Year, template);
            var cleanedName = PathSanitizer.SanitizeFileName(formatted) + extension;

            var preview = new FilePreview
            {
                OriginalName = fileName,
                OriginalFilePath = file,
                FileSizeBytes = fileInfo.Length,
                Year = parsed.Year,
                CleanedName = cleanedName,
                Confidence = parsed.Confidence,
                IsReviewed = false,
                IsSelected = true,
                IsTvEpisode = RegexPatterns.IsTvEpisode(fileName)
            };

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
        var companionExtensions = new[] { ".srt", ".sub", ".ass", ".ssa", ".vtt", ".idx" };
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
                continue;  // no change needed
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

                // Rename companion files (same base name, different extension)
                var oldBase = Path.GetFileNameWithoutExtension(oldFilePath);
                var newBase = Path.GetFileNameWithoutExtension(p.CleanedName);
                foreach (var ext in companionExtensions)
                {
                    var oldCompanion = Path.Combine(dir, oldBase + ext);
                    if (!File.Exists(oldCompanion)) continue;
                    var newCompanion = Path.Combine(dir, newBase + ext);
                    if (File.Exists(newCompanion)) continue;
                    try
                    {
                        await Task.Run(() => File.Move(oldCompanion, newCompanion));
                        undoLog?.Add(new UndoService.RenameRecord(oldCompanion, newCompanion, IsDirectory: false));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Companion rename failed: {oldCompanion} -> {newCompanion}: {ex.Message}");
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
                Confidence = preview.Confidence,
                DestinationFolder = destinationFolder,
                FinalFileName = preview.CleanedName
            });
        }

        return operations;
    }
}
