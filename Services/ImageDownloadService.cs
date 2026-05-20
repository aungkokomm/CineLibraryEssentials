using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

public class ImageDownloadService
{
    private readonly HttpClient _httpClient;
    private const int MaxRetries = 3;

    public ImageDownloadService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<bool> DownloadPosterAsync(string imageUrl, string outputPath)
    {
        if (string.IsNullOrEmpty(imageUrl))
            return false;

        var fileName = $"{Path.GetFileNameWithoutExtension(outputPath)}-poster.jpg";
        var fullPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? "", fileName);

        return await DownloadImageAsync(imageUrl, fullPath);
    }

    public async Task<bool> DownloadFanartAsync(string imageUrl, string outputPath)
    {
        if (string.IsNullOrEmpty(imageUrl))
            return false;

        var fileName = $"{Path.GetFileNameWithoutExtension(outputPath)}-fanart.jpg";
        var fullPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? "", fileName);

        return await DownloadImageAsync(imageUrl, fullPath);
    }

    /// <summary>
    /// Downloads every cast member's headshot in parallel (5 concurrent) at full
    /// TMDb resolution. Idempotent: skips actors whose photo already exists on
    /// disk, so a re-scrape only fills in the gaps. Uses the Kodi/Plex/Jellyfin
    /// "Firstname_Lastname.jpg" filename convention so the standard NFO
    /// &lt;actor&gt;&lt;thumb&gt; refs resolve cleanly.
    /// </summary>
    public async Task<(int downloaded, int failed)> DownloadActorPhotosAsync(
        List<CastMember> cast,
        string actorsFolderPath)
    {
        if (!Directory.Exists(actorsFolderPath))
            Directory.CreateDirectory(actorsFolderPath);

        // Only cast members with a TMDb profile photo. (Many minor roles have no
        // profile_path — those can't be downloaded and we don't pretend to.)
        var candidates = cast.Where(a => !string.IsNullOrEmpty(a.ProfilePath)).ToList();
        if (candidates.Count == 0) return (0, 0);

        // 5 concurrent HTTP downloads is the sweet spot: well under TMDb's
        // 40 req / 10 sec rate ceiling, fast enough that a 30-actor movie
        // finishes in seconds instead of a minute.
        using var gate = new SemaphoreSlim(5, 5);
        int downloaded = 0, failed = 0;

        var tasks = candidates.Select(async actor =>
        {
            await gate.WaitAsync();
            try
            {
                var fileName = BuildActorFileName(actor.Name);
                var filePath = Path.Combine(actorsFolderPath, fileName);

                // Skip if we already have it — makes re-scrapes near-instant.
                if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                {
                    Interlocked.Increment(ref downloaded);
                    return;
                }

                var imageUrl = $"https://image.tmdb.org/t/p/original{actor.ProfilePath}";
                if (await DownloadImageAsync(imageUrl, filePath))
                    Interlocked.Increment(ref downloaded);
                else
                    Interlocked.Increment(ref failed);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return (downloaded, failed);
    }

    /// <summary>
    /// Builds the Kodi/Plex/Jellyfin-standard ".actors/" filename for an actor.
    /// Spaces become underscores, filesystem-invalid chars are stripped, so
    /// "Robert De Niro" → "Robert_De_Niro.jpg" and "J.K. Simmons" → "J.K._Simmons.jpg".
    /// </summary>
    public static string BuildActorFileName(string actorName)
    {
        var safe = new System.Text.StringBuilder(actorName.Length);
        foreach (var c in actorName)
        {
            if (c == ' ') safe.Append('_');
            else if (Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0) safe.Append(c);
        }
        var name = safe.ToString().Trim('_');
        if (string.IsNullOrEmpty(name)) name = "actor";
        return name + ".jpg";
    }

    /// <summary>
    /// Public wrapper around the retrying downloader for callers (TV scraper)
    /// that need to fetch a single image at a known URL.
    /// </summary>
    public Task<bool> DownloadAnyImageAsync(string imageUrl, string outputPath)
        => DownloadImageAsync(imageUrl, outputPath);

    private async Task<bool> DownloadImageAsync(string imageUrl, string outputPath)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var response = await _httpClient.GetAsync(imageUrl);
                if (!response.IsSuccessStatusCode)
                    continue;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

                await contentStream.CopyToAsync(fileStream);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Download attempt {attempt + 1} failed: {ex.Message}");
                if (attempt == MaxRetries - 1)
                    System.Diagnostics.Debug.WriteLine($"Failed to download: {imageUrl}");
            }
        }

        return false;
    }
}
