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

    public async Task<(int downloaded, int failed)> DownloadActorPhotosAsync(
        List<CastMember> cast,
        string actorsFolderPath)
    {
        int downloaded = 0;
        int failed = 0;

        if (!Directory.Exists(actorsFolderPath))
            Directory.CreateDirectory(actorsFolderPath);

        foreach (var actor in cast.Take(10)) // Limit to top 10 actors
        {
            if (string.IsNullOrEmpty(actor.ProfilePath))
                continue;

            var fileName = $"{actor.Name.Replace(" ", "-")}.jpg";
            var filePath = Path.Combine(actorsFolderPath, fileName);

            var imageUrl = $"https://image.tmdb.org/t/p/w185{actor.ProfilePath}";

            if (await DownloadImageAsync(imageUrl, filePath))
                downloaded++;
            else
                failed++;
        }

        return (downloaded, failed);
    }

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
