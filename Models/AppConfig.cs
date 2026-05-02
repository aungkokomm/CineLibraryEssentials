namespace CineLibraryEssentials.Models;

public class AppConfig
{
    public string? TmdbApiKey { get; set; }
    public string LastOutputPath { get; set; } = string.Empty;
    public List<string> SupportedFormats { get; set; } = new();
}
