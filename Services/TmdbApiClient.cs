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

        // Single batched call: append credits (cast + crew), release_dates (for
        // MPAA certification), and videos (for trailer URL). Gives us everything
        // MediaElch writes to the NFO in one request.
        var url = $"{BaseUrl}/movie/{tmdbId}?api_key={_apiKey}" +
                  $"&append_to_response=credits,release_dates,videos";

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var metadata = JsonSerializer.Deserialize<MovieMetadata>(json, options);
            if (metadata == null) return null;

            // Parse the appended sub-objects that don't deserialize automatically
            // because they live under nested response keys.
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("credits", out var creditsEl))
            {
                metadata.Cast = ParseCast(creditsEl);
                ParseCrew(creditsEl, metadata);
            }

            if (root.TryGetProperty("release_dates", out var rdEl))
                metadata.Certification = ParseCertification(rdEl);

            if (root.TryGetProperty("videos", out var videosEl))
                metadata.TrailerUrl = ParseTrailerUrl(videosEl);

            return metadata;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting movie details: {ex.Message}");
            return null;
        }
    }

    private static List<CastMember> ParseCast(JsonElement creditsEl)
    {
        var list = new List<CastMember>();
        if (!creditsEl.TryGetProperty("cast", out var castArr)) return list;

        foreach (var m in castArr.EnumerateArray().Take(50))
        {
            list.Add(new CastMember
            {
                Id = m.TryGetProperty("id", out var i) ? i.GetInt32() : 0,
                Name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Character = m.TryGetProperty("character", out var c) ? c.GetString() ?? "" : "",
                ProfilePath = m.TryGetProperty("profile_path", out var pp) ? pp.GetString() : null,
                Order = m.TryGetProperty("order", out var o) ? o.GetInt32() : 0,
            });
        }
        return list;
    }

    /// <summary>
    /// Splits credits.crew into Directors / Writers / Producers — the three
    /// groups Kodi/MediaElch surface in the NFO. Directors are by job, the
    /// other two are by department because TMDb encodes them as roles.
    /// </summary>
    private static void ParseCrew(JsonElement creditsEl, MovieMetadata metadata)
    {
        if (!creditsEl.TryGetProperty("crew", out var crewArr)) return;

        foreach (var m in crewArr.EnumerateArray())
        {
            var member = new CrewMember
            {
                Id = m.TryGetProperty("id", out var i) ? i.GetInt32() : 0,
                Name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Job = m.TryGetProperty("job", out var j) ? j.GetString() ?? "" : "",
                Department = m.TryGetProperty("department", out var d) ? d.GetString() ?? "" : "",
                ProfilePath = m.TryGetProperty("profile_path", out var pp) ? pp.GetString() : null,
            };

            if (string.Equals(member.Job, "Director", StringComparison.OrdinalIgnoreCase))
                metadata.Directors.Add(member);
            else if (string.Equals(member.Department, "Writing", StringComparison.OrdinalIgnoreCase))
                metadata.Writers.Add(member);
            else if (string.Equals(member.Department, "Production", StringComparison.OrdinalIgnoreCase)
                     && member.Job.Contains("Producer", StringComparison.OrdinalIgnoreCase))
                metadata.Producers.Add(member);
        }
    }

    /// <summary>
    /// Picks the MPAA certification from the release_dates response. Prefers
    /// the US theatrical rating (type 3) — MediaElch defaults to the same so
    /// the &lt;mpaa&gt; tag is comparable across scrapers.
    /// </summary>
    private static string ParseCertification(JsonElement rdEl)
    {
        if (!rdEl.TryGetProperty("results", out var resultsArr)) return string.Empty;

        // First pass: look for US
        foreach (var region in resultsArr.EnumerateArray())
        {
            if (!region.TryGetProperty("iso_3166_1", out var iso)) continue;
            if (!string.Equals(iso.GetString(), "US", StringComparison.OrdinalIgnoreCase)) continue;
            if (!region.TryGetProperty("release_dates", out var dates)) continue;
            foreach (var d in dates.EnumerateArray())
            {
                if (d.TryGetProperty("certification", out var cert))
                {
                    var s = cert.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }

        // Fallback: first non-empty certification from any region
        foreach (var region in resultsArr.EnumerateArray())
        {
            if (!region.TryGetProperty("release_dates", out var dates)) continue;
            foreach (var d in dates.EnumerateArray())
            {
                if (d.TryGetProperty("certification", out var cert))
                {
                    var s = cert.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Picks the official YouTube trailer URL from the videos response, falling
    /// back to any teaser if no trailer is flagged official.
    /// </summary>
    private static string ParseTrailerUrl(JsonElement videosEl)
    {
        if (!videosEl.TryGetProperty("results", out var resultsArr)) return string.Empty;

        string? officialTrailer = null;
        string? anyTrailer = null;
        string? anyTeaser = null;

        foreach (var v in resultsArr.EnumerateArray())
        {
            var site = v.TryGetProperty("site", out var s) ? s.GetString() : null;
            if (!string.Equals(site, "YouTube", StringComparison.OrdinalIgnoreCase)) continue;

            var key = v.TryGetProperty("key", out var k) ? k.GetString() : null;
            if (string.IsNullOrEmpty(key)) continue;

            var type = v.TryGetProperty("type", out var t) ? t.GetString() : null;
            var official = v.TryGetProperty("official", out var o) && o.GetBoolean();
            var url = $"https://www.youtube.com/watch?v={key}";

            if (string.Equals(type, "Trailer", StringComparison.OrdinalIgnoreCase))
            {
                if (official) { officialTrailer ??= url; }
                anyTrailer ??= url;
            }
            else if (string.Equals(type, "Teaser", StringComparison.OrdinalIgnoreCase))
            {
                anyTeaser ??= url;
            }
        }

        return officialTrailer ?? anyTrailer ?? anyTeaser ?? string.Empty;
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

            // Keep the FULL cast list as TMDb returns it (already sorted by billing
            // order). The previous .Take(10) is what made CineLibrary Essentials look
            // sparse compared to MediaElch. We cap at 50 below as a sanity ceiling for
            // huge ensemble casts — far above the ~15-25 useful entries per movie.
            var cast = new List<CastMember>();
            if (root.TryGetProperty("cast", out var castArray))
            {
                foreach (var member in castArray.EnumerateArray().Take(50))
                {
                    var name = member.TryGetProperty("name", out var n) ? n.GetString() : "";
                    var character = member.TryGetProperty("character", out var c) ? c.GetString() : "";
                    var profilePath = member.TryGetProperty("profile_path", out var pp) ? pp.GetString() : null;
                    var id = member.TryGetProperty("id", out var mid) ? mid.GetInt32() : 0;
                    var order = member.TryGetProperty("order", out var o) ? o.GetInt32() : 0;

                    cast.Add(new CastMember
                    {
                        Id = id,
                        Name = name ?? "",
                        Character = character ?? "",
                        ProfilePath = profilePath,
                        Order = order
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

    /// <summary>
    /// Builds a TMDb image URL. Default is "original" (the full uploaded asset)
    /// because library scrapers (Plex/Kodi/Jellyfin) all expect full-resolution
    /// artwork — lower sizes look blurry on modern displays. Callers can still
    /// override (e.g. small thumbnails in the TMDb search picker use "w185").
    /// Valid sizes: posters w92/w154/w185/w342/w500/w780/original,
    /// backdrops w300/w780/w1280/original, profiles w45/w185/h632/original.
    /// </summary>
    public string GetImageUrl(string? imagePath, string size = "original")
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
