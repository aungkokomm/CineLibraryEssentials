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

        int organized = 0, merged = 0;

        foreach (var operation in operations)
        {
            try
            {
                // CreateDirectory is idempotent — if the destination folder is already
                // there (which is what enables folder merging), this is a no-op.
                if (!Directory.Exists(operation.DestinationFolder))
                    Directory.CreateDirectory(operation.DestinationFolder);

                var destinationFile = Path.Combine(operation.DestinationFolder, operation.FinalFileName);

                if (File.Exists(destinationFile))
                {
                    var srcFull = Path.GetFullPath(operation.OriginalFilePath);
                    var destFull = Path.GetFullPath(destinationFile);
                    var sameFile = string.Equals(srcFull, destFull, StringComparison.OrdinalIgnoreCase);

                    if (sameFile)
                    {
                        // The user re-ran the wizard on an already-organized folder —
                        // nothing to move, but honour the metadata-clean preference.
                        if (metadataCleaner != null)
                        {
                            var metaTitle = Path.GetFileNameWithoutExtension(operation.FinalFileName);
                            var meta = metadataCleaner.Clean(destinationFile, metaTitle);
                            if (!meta.Success && !string.IsNullOrEmpty(meta.Error))
                                result.Errors.Add($"{operation.OriginalFileName}: metadata clean failed: {meta.Error}");
                        }
                        continue;
                    }

                    // -------- Folder merge --------
                    // A different file is already at the destination with the same
                    // name. The user explicitly wants this case to merge (not error,
                    // not duplicate): keep the existing file as authoritative, but
                    // still bring in any companion subtitles / extras from the source
                    // folder that aren't already there. After the merge, leave the
                    // source video file in place so the user can manually inspect /
                    // decide which copy to keep.
                    await MoveCompanionFilesAsync(
                        operation.OriginalFilePath,
                        operation.DestinationFolder,
                        operation.FinalFileName);
                    merged++;
                    continue;
                }

                File.Move(operation.OriginalFilePath, destinationFile, overwrite: false);
                organized++;

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

        // Build a result message that distinguishes fresh moves from merges so
        // the user knows what happened.
        var parts = new List<string>();
        if (organized > 0) parts.Add($"{organized} organized");
        if (merged > 0)    parts.Add($"{merged} merged into existing folder(s)");
        if (result.Errors.Count > 0) parts.Add($"{result.Errors.Count} error(s)");
        result.Message = parts.Count > 0 ? string.Join(" · ", parts) : "Nothing to do";

        return result;
    }

    private async Task MoveCompanionFilesAsync(string originalVideoPath, string destinationFolder, string renamedVideoFile)
    {
        var sourceDir = Path.GetDirectoryName(originalVideoPath);
        if (string.IsNullOrEmpty(sourceDir))
            return;

        var videoNameWithoutExt = Path.GetFileNameWithoutExtension(originalVideoPath);
        var renamedVideoWithoutExt = Path.GetFileNameWithoutExtension(renamedVideoFile);
        var companionExtensions = new[] { ".srt", ".sub", ".ass", ".ssa", ".vtt", ".idx" };

        foreach (var ext in companionExtensions)
        {
            var companionFile = Path.Combine(sourceDir, $"{videoNameWithoutExt}{ext}");
            if (!File.Exists(companionFile)) continue;

            var destCompanionFile = Path.Combine(destinationFolder, $"{renamedVideoWithoutExt}{ext}");

            // Merge case: a companion with the same name is already at the destination.
            // Preserve the existing one (don't destroy data); leave the source alongside
            // the source video so nothing is lost.
            if (File.Exists(destCompanionFile))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Companion already present at destination, leaving source in place: {destCompanionFile}");
                continue;
            }

            try
            {
                File.Move(companionFile, destCompanionFile, overwrite: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error moving companion file: {ex.Message}");
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
