using System.Runtime.InteropServices;
using System.Text;

namespace CineLibraryEssentials.Services;

/// <summary>
/// "Copy diagnostics" in About: app + Windows version, the settings that change
/// behaviour, and the problems the user saw this session (warning/error toasts,
/// failed scrapes, background errors). Never includes the API key. Kept in memory only.
/// </summary>
public static class DiagnosticsService
{
    private const int MaxRecent = 25;
    private static readonly Queue<string> _recent = new();
    private static readonly object _lock = new();

    /// <summary>Remembers a problem the user saw (newest 25 kept).</summary>
    public static void Record(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_lock)
        {
            _recent.Enqueue($"{DateTime.Now:HH:mm:ss}  {message.Trim()}");
            while (_recent.Count > MaxRecent) _recent.Dequeue();
        }
    }

    public static string Build(ConfigService config, string appVersion)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CineLibrary Essentials {appVersion}");
        sb.AppendLine($"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Display language: {System.Globalization.CultureInfo.CurrentUICulture.Name}, formats: {System.Globalization.CultureInfo.CurrentCulture.Name}");
        sb.AppendLine();
        sb.AppendLine("Settings");
        sb.AppendLine($"  TMDb key: {(config.HasOwnApiKey() ? "your own" : "built-in")}");
        sb.AppendLine($"  TMDb language: {config.GetScrapeLanguage()}");
        sb.AppendLine($"  Main rating: {(config.GetPreferImdbRating() ? "IMDb" : "TMDb")}");
        sb.AppendLine($"  Episode titles in file names: {OnOff(config.GetAddEpisodeTitles())}");
        sb.AppendLine($"  Mode: {config.GetWizardMode()}");
        sb.AppendLine($"  File name format: {config.GetLastTemplate()}");
        sb.AppendLine($"  Subfolders by default: {OnOff(config.GetRecursiveScanDefault())}");
        sb.AppendLine($"  Clean embedded metadata: {OnOff(config.GetCleanEmbeddedMetadata())}");
        sb.AppendLine($"  Words to always remove: {config.GetCustomRemoveWords().Count}");
        sb.AppendLine($"  Check for updates: {OnOff(config.GetAutoCheckForUpdates())}");
        sb.AppendLine();

        string[] recent;
        lock (_lock) recent = _recent.ToArray();
        sb.AppendLine(recent.Length == 0 ? "No problems this session." : "Problems this session (newest last)");
        foreach (var line in recent) sb.AppendLine("  " + line);
        return sb.ToString().TrimEnd();
    }

    private static string OnOff(bool value) => value ? "on" : "off";
}
