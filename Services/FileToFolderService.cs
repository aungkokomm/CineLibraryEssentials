using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

public class FileToFolderService
{
    public async Task<ProcessingResult> OrganizeFilesAsync(List<FileOperation> operations)
    {
        var result = new ProcessingResult { Success = true };

        foreach (var operation in operations)
        {
            try
            {
                // Create destination folder
                if (!Directory.Exists(operation.DestinationFolder))
                {
                    Directory.CreateDirectory(operation.DestinationFolder);
                }

                // Get final destination file path
                var destinationFile = Path.Combine(operation.DestinationFolder, operation.FinalFileName);

                // Check if file already exists
                if (File.Exists(destinationFile))
                {
                    result.Errors.Add($"Destination already exists: {destinationFile}");
                    continue;
                }

                // Move the file
                File.Move(operation.OriginalFilePath, destinationFile, overwrite: false);

                // Move companion files (subtitles, etc.)
                await MoveCompanionFilesAsync(operation.OriginalFilePath, operation.DestinationFolder, operation.FinalFileName);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"Error processing {operation.OriginalFileName}: {ex.Message}");
            }
        }

        result.Message = result.Errors.Count == 0 ? "All files organized successfully" : $"{result.Errors.Count} errors occurred";

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
