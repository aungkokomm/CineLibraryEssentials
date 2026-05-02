using System.Text.Json;
using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

public class TmdbApiClient
{
    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const int RequestDelay = 250; // milliseconds between requests
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private DateTime _lastRequestTime = DateTime.MinValue;

    public TmdbApiClient(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient();
    }

    public async Task<List<MovieMetadata>> SearchMovieAsync(string title, int? year = null)
    {
        await RateLimitAsync();

        var query = System.Web.HttpUtility.UrlEncode(title);
        var url = $"{BaseUrl}/search/movie?api_key={_apiKey}&query={query}";

        if (year.HasValue)
            url += $"&primary_release_year={year}";

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TmdbSearchResult>(json);

            return result?.Results ?? new List<MovieMetadata>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error searching TMDb: {ex.Message}");
            return new List<MovieMetadata>();
        }
    }

    public async Task<MovieMetadata?> GetMovieDetailsAsync(int tmdbId)
    {
        await RateLimitAsync();

        var url = $"{BaseUrl}/movie/{tmdbId}?api_key={_apiKey}&append_to_response=credits";

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var metadata = JsonSerializer.Deserialize<MovieMetadata>(json, options);

            return metadata;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting movie details: {ex.Message}");
            return null;
        }
    }

    public async Task<List<CastMember>> GetMovieCastAsync(int tmdbId)
    {
        await RateLimitAsync();

        var url = $"{BaseUrl}/movie/{tmdbId}/credits?api_key={_apiKey}";

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var cast = new List<CastMember>();
            if (root.TryGetProperty("cast", out var castArray))
            {
                foreach (var member in castArray.EnumerateArray().Take(10))
                {
                    var name = member.TryGetProperty("name", out var n) ? n.GetString() : "";
                    var character = member.TryGetProperty("character", out var c) ? c.GetString() : "";
                    var profilePath = member.TryGetProperty("profile_path", out var pp) ? pp.GetString() : null;
                    var id = member.TryGetProperty("id", out var mid) ? mid.GetInt32() : 0;

                    cast.Add(new CastMember
                    {
                        Id = id,
                        Name = name ?? "",
                        Character = character ?? "",
                        ProfilePath = profilePath
                    });
                }
            }

            return cast;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting cast: {ex.Message}");
            return new List<CastMember>();
        }
    }

    public string GetImageUrl(string? imagePath, string size = "w500")
    {
        if (string.IsNullOrEmpty(imagePath))
            return string.Empty;

        return $"https://image.tmdb.org/t/p/{size}{imagePath}";
    }

    private async Task RateLimitAsync()
    {
        var elapsed = DateTime.Now - _lastRequestTime;
        if (elapsed.TotalMilliseconds < RequestDelay)
        {
            await Task.Delay((int)(RequestDelay - elapsed.TotalMilliseconds));
        }

        _lastRequestTime = DateTime.Now;
    }
}
