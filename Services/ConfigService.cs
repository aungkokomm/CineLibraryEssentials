using System.Text.Json;
using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

public class ConfigService
{
    private const string ConfigFileName = "appsettings.json";
    private const string DefaultTmdbApiKey = "bbbafb01eb3938531c9270a7147fbb5f";
    private const int RecentFoldersMax = 10;

    private readonly string _configPath;
    private AppConfig _config;

    public ConfigService()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        _config = LoadConfig();

        // Ensure API key is always set
        if (string.IsNullOrEmpty(_config.TmdbApiKey))
        {
            _config.TmdbApiKey = DefaultTmdbApiKey;
            SaveConfig();
        }
    }

    // ----- API key -----
    public string? GetApiKey() => _config.TmdbApiKey ?? DefaultTmdbApiKey;
    public void SetApiKey(string apiKey) { _config.TmdbApiKey = apiKey; SaveConfig(); }

    // ----- Output path (legacy) -----
    public string GetLastOutputPath() => _config.LastOutputPath;
    public void SetLastOutputPath(string path) { _config.LastOutputPath = path; SaveConfig(); }

    public List<string> GetSupportedFormats()
    {
        if (_config.SupportedFormats == null || _config.SupportedFormats.Count == 0)
            _config.SupportedFormats = new() { ".mp4", ".mkv", ".avi", ".mov", ".flv", ".wmv", ".webm", ".m4v" };
        return _config.SupportedFormats;
    }

    // ----- Recent folders (v1.1) -----
    public IReadOnlyList<string> GetRecentSourceFolders() => _config.RecentSourceFolders;
    public IReadOnlyList<string> GetRecentOutputFolders() => _config.RecentOutputFolders;

    public void AddRecentSourceFolder(string path) => AddRecent(_config.RecentSourceFolders, path);
    public void AddRecentOutputFolder(string path) => AddRecent(_config.RecentOutputFolders, path);

    private void AddRecent(List<string> list, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        // Move to top if exists, else insert
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > RecentFoldersMax) list.RemoveRange(RecentFoldersMax, list.Count - RecentFoldersMax);
        SaveConfig();
    }

    // ----- Window position (v1.1) -----
    public (int width, int height, int x, int y) GetWindowBounds()
        => (_config.WindowWidth, _config.WindowHeight, _config.WindowX, _config.WindowY);

    public void SetWindowBounds(int width, int height, int x, int y)
    {
        _config.WindowWidth = width;
        _config.WindowHeight = height;
        _config.WindowX = x;
        _config.WindowY = y;
        SaveConfig();
    }

    // ----- Dismissed warnings (v1.1) -----
    public bool IsWarningDismissed(string warningId)
        => _config.DismissedWarnings.Contains(warningId);

    public void DismissWarning(string warningId)
    {
        if (!_config.DismissedWarnings.Contains(warningId))
        {
            _config.DismissedWarnings.Add(warningId);
            SaveConfig();
        }
    }

    // ----- Step 3 view preference (v1.1) -----
    public string GetPreferredStep3View() => _config.PreferredStep3View;
    public void SetPreferredStep3View(string view) { _config.PreferredStep3View = view; SaveConfig(); }

    // ----- Last template (v1.1) -----
    public string GetLastTemplate() => string.IsNullOrEmpty(_config.LastTemplate)
        ? "{Title} ({Year})" : _config.LastTemplate;
    public void SetLastTemplate(string t) { _config.LastTemplate = t; SaveConfig(); }

    // ----- Clean embedded metadata default (v1.1) -----
    public bool GetCleanEmbeddedMetadata() => _config.CleanEmbeddedMetadata;
    public void SetCleanEmbeddedMetadata(bool v) { _config.CleanEmbeddedMetadata = v; SaveConfig(); }

    // ----- Step 1 sort (v1.1) -----
    public (string column, bool descending) GetStep1Sort()
        => (_config.Step1SortColumn, _config.Step1SortDescending);

    public void SetStep1Sort(string column, bool descending)
    {
        _config.Step1SortColumn = column;
        _config.Step1SortDescending = descending;
        SaveConfig();
    }

    // ----- Auto-update check (v1.1.10) -----
    public DateTime GetLastUpdateCheck() =>
        _config.LastUpdateCheckUtcTicks == 0
            ? DateTime.MinValue
            : new DateTime(_config.LastUpdateCheckUtcTicks, DateTimeKind.Utc);

    public void SetLastUpdateCheckNow()
    {
        _config.LastUpdateCheckUtcTicks = DateTime.UtcNow.Ticks;
        SaveConfig();
    }

    public string GetSkippedUpdateVersion() => _config.SkippedUpdateVersion ?? string.Empty;
    public void SetSkippedUpdateVersion(string v)
    {
        _config.SkippedUpdateVersion = v ?? string.Empty;
        SaveConfig();
    }

    // ----- Settings page (v1.1.11) -----
    public string GetScrapeLanguage() =>
        string.IsNullOrWhiteSpace(_config.ScrapeLanguage) ? "en" : _config.ScrapeLanguage;
    public void SetScrapeLanguage(string code)
    {
        _config.ScrapeLanguage = string.IsNullOrWhiteSpace(code) ? "en" : code;
        SaveConfig();
    }

    public bool GetAutoCheckForUpdates() => _config.AutoCheckForUpdates;
    public void SetAutoCheckForUpdates(bool v) { _config.AutoCheckForUpdates = v; SaveConfig(); }

    public bool GetRecursiveScanDefault() => _config.RecursiveScanDefault;
    public void SetRecursiveScanDefault(bool v) { _config.RecursiveScanDefault = v; SaveConfig(); }

    // ----- Persistence -----
    private AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
        }
        return new AppConfig();
    }

    private void SaveConfig()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_configPath, JsonSerializer.Serialize(_config, options));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
        }
    }
}
