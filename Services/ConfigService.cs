using System.Text.Json;
using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

public class ConfigService
{
    private const string ConfigFileName = "appsettings.json";
    private const string DefaultTmdbApiKey = "bbbafb01eb3938531c9270a7147fbb5f";
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

    public string? GetApiKey() => _config.TmdbApiKey ?? DefaultTmdbApiKey;

    public void SetApiKey(string apiKey)
    {
        _config.TmdbApiKey = apiKey;
        SaveConfig();
    }

    public string GetLastOutputPath() => _config.LastOutputPath;

    public void SetLastOutputPath(string path)
    {
        _config.LastOutputPath = path;
        SaveConfig();
    }

    public List<string> GetSupportedFormats()
    {
        if (_config.SupportedFormats == null || _config.SupportedFormats.Count == 0)
        {
            _config.SupportedFormats = new() { ".mp4", ".mkv", ".avi", ".mov", ".flv", ".wmv", ".webm", ".m4v" };
        }
        return _config.SupportedFormats;
    }

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
            var json = JsonSerializer.Serialize(_config, options);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
        }
    }
}
