namespace CineLibraryEssentials.Utilities;

public class PathSanitizer
{
    private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public static string SanitizeFolderName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Untitled";

        var sanitized = new string(input
            .Where(c => !InvalidPathChars.Contains(c) && !InvalidFileNameChars.Contains(c))
            .ToArray());

        sanitized = sanitized.Trim();

        // Replace problematic sequences
        sanitized = sanitized.Replace("  ", " ");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[\s]+", " ");

        if (string.IsNullOrWhiteSpace(sanitized))
            return "Untitled";

        // Trim to max length (Windows path limit)
        if (sanitized.Length > 100)
            sanitized = sanitized.Substring(0, 100).TrimEnd();

        return sanitized;
    }

    public static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "untitled";

        var sanitized = new string(input
            .Where(c => !InvalidFileNameChars.Contains(c))
            .ToArray());

        sanitized = sanitized.Trim();
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[\s]+", " ");

        if (string.IsNullOrWhiteSpace(sanitized))
            return "untitled";

        if (sanitized.Length > 200)
            sanitized = sanitized.Substring(0, 200).TrimEnd();

        return sanitized;
    }

    public static string CreateMovieFolderName(string title, int year)
    {
        var sanitized = SanitizeFolderName(title);
        if (year > 0)
            return $"{sanitized} ({year})";
        return sanitized;
    }

    public static string CreateMovieFileName(string title, int year, string extension)
    {
        var sanitized = SanitizeFileName(title);
        if (year > 0)
            return $"{sanitized} ({year}){extension}";
        return $"{sanitized}{extension}";
    }
}
