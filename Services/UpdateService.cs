using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace CineLibraryEssentials.Services;

/// <summary>
/// Lightweight update checker. Polls the GitHub Releases API for the latest
/// tag and compares it against the running assembly version. No auto-download —
/// just tells the user "newer version available" and points to the releases page.
/// </summary>
public class UpdateService
{
    private const string OwnerRepo = "aungkokomm/CineLibraryEssentials";
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/" + OwnerRepo + "/releases/latest";
    private const string ReleasesPageUrl =
        "https://github.com/" + OwnerRepo + "/releases";

    public class UpdateInfo
    {
        public bool Success { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = ReleasesPageUrl;
        public string? ReleaseNotes { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Queries GitHub for the latest release. Safe to call from UI thread — runs
    /// async and never throws (errors are surfaced via <see cref="UpdateInfo.Error"/>).
    /// </summary>
    public async Task<UpdateInfo> CheckForUpdateAsync()
    {
        var info = new UpdateInfo();

        // Read current version from the running assembly. AssemblyVersion is set
        // in the .csproj (e.g. 1.1.5.0) — we only care about Major.Minor.Build.
        var current = typeof(UpdateService).Assembly.GetName().Version;
        if (current == null)
        {
            info.Error = "Could not read current assembly version.";
            return info;
        }
        info.CurrentVersion = $"{current.Major}.{current.Minor}.{current.Build}";

        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            // GitHub API requires a User-Agent header.
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("CineLibraryEssentials", info.CurrentVersion));
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var resp = await http.GetAsync(LatestReleaseUrl);
            if (!resp.IsSuccessStatusCode)
            {
                info.Error = resp.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "No releases published yet."
                    : $"GitHub returned {(int)resp.StatusCode} {resp.StatusCode}.";
                return info;
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var htmlUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;

            // Tags can be like "v1.1.5" or "1.1.5" — strip a leading v if present.
            var latestStr = tagName.TrimStart('v', 'V').Trim();
            info.LatestVersion = latestStr;
            if (!string.IsNullOrEmpty(htmlUrl))
                info.ReleaseUrl = htmlUrl;
            info.ReleaseNotes = body;

            // Pad to 4 parts so Version.Parse is happy: "1.1.5" -> "1.1.5.0".
            var paddedLatest = PadToFourParts(latestStr);
            var paddedCurrent = $"{current.Major}.{current.Minor}.{current.Build}.0";

            if (Version.TryParse(paddedLatest, out var latestVer)
                && Version.TryParse(paddedCurrent, out var currentVer))
            {
                info.IsUpdateAvailable = latestVer > currentVer;
                info.Success = true;
            }
            else
            {
                info.Error = $"Could not parse version '{tagName}'.";
            }
        }
        catch (TaskCanceledException)
        {
            info.Error = "Network request timed out.";
        }
        catch (HttpRequestException ex)
        {
            info.Error = $"Network error: {ex.Message}";
        }
        catch (Exception ex)
        {
            info.Error = ex.Message;
        }

        return info;
    }

    private static string PadToFourParts(string version)
    {
        var parts = version.Split('.');
        return parts.Length switch
        {
            1 => $"{parts[0]}.0.0.0",
            2 => $"{parts[0]}.{parts[1]}.0.0",
            3 => $"{parts[0]}.{parts[1]}.{parts[2]}.0",
            _ => version
        };
    }
}
