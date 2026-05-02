namespace CineLibraryEssentials.Utilities;

public class FileFormatValidator
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".flv", ".wmv", ".webm", ".m4v"
    };

    public static bool IsVideoFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension);
    }

    public static bool IsVideoFile(string filePath, out string? extension)
    {
        extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension);
    }

    public static IEnumerable<string> GetSupportedFormats() => SupportedExtensions;
}
