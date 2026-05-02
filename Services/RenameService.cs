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
    /// </summary>
    public async Task<ProcessingResult> RenameInPlaceAsync(IEnumerable<FilePreview> previews)
    {
        var result = new ProcessingResult { Success = true };
        var companionExtensions = new[] { ".srt", ".sub", ".ass", ".ssa", ".vtt", ".idx" };

        foreach (var p in previews)
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
                await Task.Run(() => File.Move(p.OriginalFilePath, newPath));

                // Rename companion files (same base name, different extension)
                var oldBase = Path.GetFileNameWithoutExtension(p.OriginalFilePath);
                var newBase = Path.GetFileNameWithoutExtension(p.CleanedName);
                foreach (var ext in companionExtensions)
                {
                    var oldCompanion = Path.Combine(dir, oldBase + ext);
                    if (!File.Exists(oldCompanion)) continue;
                    var newCompanion = Path.Combine(dir, newBase + ext);
                    if (File.Exists(newCompanion)) continue;
                    try { await Task.Run(() => File.Move(oldCompanion, newCompanion)); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Companion rename failed: {oldCompanion} -> {newCompanion}: {ex.Message}");
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
