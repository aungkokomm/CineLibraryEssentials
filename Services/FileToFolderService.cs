using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

public class FileToFolderService
{
    public async Task<ProcessingResult> OrganizeFilesAsync(
        List<FileOperation> operations,
        bool cleanEmbeddedMetadata = false)
    {
        var result = new ProcessingResult { Success = true };
        var metadataCleaner = cleanEmbeddedMetadata ? new MetadataCleanerService() : null;

        foreach (var operation in operations)
        {
            try
            {
                if (!Directory.Exists(operation.DestinationFolder))
                    Directory.CreateDirectory(operation.DestinationFolder);

                var destinationFile = Path.Combine(operation.DestinationFolder, operation.FinalFileName);

                if (File.Exists(destinationFile))
                {
                    // If the destination IS the source file (user re-ran the wizard
                    // on an already-organized folder), there's nothing to move —
                    // but we still want to clean metadata if they asked for it.
                    var srcFull = Path.GetFullPath(operation.OriginalFilePath);
                    var destFull = Path.GetFullPath(destinationFile);
                    var sameFile = string.Equals(srcFull, destFull, StringComparison.OrdinalIgnoreCase);

                    if (sameFile && metadataCleaner != null)
                    {
                        var metaTitle = Path.GetFileNameWithoutExtension(operation.FinalFileName);
                        var meta = metadataCleaner.Clean(destinationFile, metaTitle);
                        if (!meta.Success && !string.IsNullOrEmpty(meta.Error))
                            result.Errors.Add($"{operation.OriginalFileName}: metadata clean failed: {meta.Error}");
                    }
                    else if (!sameFile)
                    {
                        result.Errors.Add($"Destination already exists: {destinationFile}");
                    }
                    continue;
                }

                File.Move(operation.OriginalFilePath, destinationFile, overwrite: false);

                await MoveCompanionFilesAsync(operation.OriginalFilePath, operation.DestinationFolder, operation.FinalFileName);

                // Optionally scrub embedded container metadata after the move.
                // Done HERE (not just in Step 1's RenameInPlaceAsync) so that users
                // who skip Step 1's "Rename Selected" and go straight to Step 2's
                // "Run File to Folder" still get a clean output file.
                if (metadataCleaner != null)
                {
                    var metaTitle = Path.GetFileNameWithoutExtension(operation.FinalFileName);
                    var meta = metadataCleaner.Clean(destinationFile, metaTitle);
                    if (!meta.Success && !string.IsNullOrEmpty(meta.Error))
                    {
                        result.Errors.Add($"{operation.OriginalFileName}: metadata clean failed: {meta.Error}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"Error processing {operation.OriginalFileName}: {ex.Message}");
            }
        }

        result.Message = result.Errors.Count == 0
            ? "All files organized successfully"
            : $"{result.Errors.Count} errors occurred";

        return result;
    }

    private async Task MoveCompanionFilesAsync(string originalVideoPath, string destinationFolder, string renamedVideoFile)
    {
        var sourceDir = Path.GetDirectoryName(originalVideoPath);
        if (string.IsNullOrEmpty(sourceDir))
            return;

        var videoNameWithoutExt = Path.GetFileNameWithoutExtension(originalVideoPath);
        var renamedVideoWithoutExt = Path.GetFileNameWithoutExtension(renamedVideoFile);
        var companionExtensions = new[] { ".srt", ".sub", ".ass", ".ssa", ".vtt" };

        foreach (var ext in companionExtensions)
        {
            var companionFile = Path.Combine(sourceDir, $"{videoNameWithoutExt}{ext}");
            if (File.Exists(companionFile))
            {
                var destCompanionFile = Path.Combine(destinationFolder, $"{renamedVideoWithoutExt}{ext}");
                try
                {
                    File.Move(companionFile, destCompanionFile, overwrite: false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error moving companion file: {ex.Message}");
                }
            }
        }

        await Task.CompletedTask;
    }

    public List<FolderPreview> GeneratePreview(List<FileOperation> operations)
    {
        var previews = new List<FolderPreview>();

        var grouped = operations.GroupBy(o => o.DestinationFolder);

        foreach (var group in grouped)
        {
            var preview = new FolderPreview
            {
                FolderName = Path.GetFileName(group.Key),
                FilesInFolder = group.Select(o => o.FinalFileName).ToList()
            };

            previews.Add(preview);
        }

        return previews.OrderBy(p => p.FolderName).ToList();
    }
}
