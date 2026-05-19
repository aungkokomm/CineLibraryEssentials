using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CineLibraryEssentials.Services;

/// <summary>
/// Update checker + one-click installer downloader. Polls the GitHub Releases
/// API, compares the latest tag against the running assembly version, and can
/// download the attached installer .exe directly so the user gets a "Download
/// and install" experience without leaving the app.
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

        /// <summary>Direct download URL for the installer .exe asset, if present.</summary>
        public string? InstallerUrl { get; set; }
        public string? InstallerFileName { get; set; }
        public long InstallerSizeBytes { get; set; }
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
            using var http = BuildHttpClient(info.CurrentVersion, TimeSpan.FromSeconds(10));

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

            // Walk assets[] for the installer .exe (prefer the one whose name
            // contains "Setup" — that's the Inno Setup output).
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                    var url = asset.TryGetProperty("browser_download_url", out var au) ? au.GetString() ?? "" : "";
                    var size = asset.TryGetProperty("size", out var asz) ? asz.GetInt64() : 0L;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                    // Prefer Setup-named installers; fall back to first .exe found.
                    if (info.InstallerUrl == null
                        || name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        info.InstallerUrl = url;
                        info.InstallerFileName = name;
                        info.InstallerSizeBytes = size;
                    }
                }
            }

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

    /// <summary>
    /// Downloads the installer .exe attached to the latest GitHub release into
    /// %TEMP% and returns the path. Reports progress as a fraction (0.0–1.0).
    /// Throws on network or I/O failure so callers can show an error.
    /// </summary>
    public async Task<string> DownloadInstallerAsync(
        UpdateInfo info,
        IProgress<double>? progress = null,
        CancellationToken cancel = default)
    {
        if (string.IsNullOrEmpty(info.InstallerUrl))
            throw new InvalidOperationException("No installer asset attached to this release.");

        var tempDir = Path.Combine(Path.GetTempPath(), "CineLibraryEssentials-Update");
        Directory.CreateDirectory(tempDir);
        var outPath = Path.Combine(tempDir,
            info.InstallerFileName ?? $"CineLibraryEssentials_Setup_{info.LatestVersion}.exe");

        // If we already downloaded this exact file (same size), reuse it.
        if (File.Exists(outPath)
            && info.InstallerSizeBytes > 0
            && new FileInfo(outPath).Length == info.InstallerSizeBytes)
        {
            progress?.Report(1.0);
            return outPath;
        }

        using var http = BuildHttpClient(info.CurrentVersion, TimeSpan.FromMinutes(10));
        using var resp = await http.GetAsync(info.InstallerUrl,
            HttpCompletionOption.ResponseHeadersRead, cancel);
        resp.EnsureSuccessStatusCode();

        var totalBytes = resp.Content.Headers.ContentLength
                         ?? (info.InstallerSizeBytes > 0 ? info.InstallerSizeBytes : -1);

        using (var src = await resp.Content.ReadAsStreamAsync(cancel))
        using (var dst = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, cancel)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), cancel);
                downloaded += read;
                if (totalBytes > 0)
                    progress?.Report(Math.Min(1.0, downloaded / (double)totalBytes));
            }
        }

        return outPath;
    }

    /// <summary>
    /// Launches the downloaded installer and exits the current process so the
    /// installer can overwrite the running .exe.
    /// </summary>
    public void LaunchInstallerAndExit(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
            // /SILENT would run an unattended install; default behavior shows the
            // usual Inno Setup wizard so the user sees what's happening.
        });

        // Give the installer a moment to spawn before we vanish.
        Task.Delay(500).Wait();
        Environment.Exit(0);
    }

    private static HttpClient BuildHttpClient(string version, TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("CineLibraryEssentials", version));
        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
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
