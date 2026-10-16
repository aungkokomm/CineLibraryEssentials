using System.Text.Json;

namespace CineLibraryEssentials.Services;

/// <summary>
/// IMDb rating + vote count for movies and TV shows, by IMDb id ("tt0111161").
///
/// IMDb's own API is paid, so this uses Agregarr's free, open-source ratings
/// proxy (no key needed), which serves IMDb's official daily
/// title.ratings dataset: https://agregarr.org/docs/imdb-ratings/
///
/// Ratings are a bonus, never a requirement: any failure (offline, timeout,
/// service down, bad response) returns no ratings instead of throwing, so a
/// scrape always completes with its TMDb data.
/// </summary>
public class ImdbRatingService
{
    private const string Endpoint = "https://api.agregarr.org/api/ratings";
    private const int MaxIdsPerRequest = 100;   // the proxy's documented batch limit

    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _http;

    public ImdbRatingService(HttpClient? http = null) => _http = http ?? SharedClient;

    public record ImdbRating(double Rating, int Votes);

    /// <summary>Rating for one title, or null when unknown or unavailable.</summary>
    public async Task<ImdbRating?> GetRatingAsync(string? imdbId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imdbId)) return null;
        var all = await GetRatingsAsync(new[] { imdbId }, ct);
        return all.TryGetValue(imdbId.Trim(), out var r) ? r : null;
    }

    /// <summary>
    /// Ratings for many titles, fetched in batches of 100. Titles IMDb has no
    /// rating for are simply missing from the result.
    /// </summary>
    public async Task<Dictionary<string, ImdbRating>> GetRatingsAsync(
        IEnumerable<string> imdbIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, ImdbRating>(StringComparer.OrdinalIgnoreCase);
        var ids = imdbIds
            .Where(IsImdbId)
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var batch in ids.Chunk(MaxIdsPerRequest))
        {
            try
            {
                var url = Endpoint + "?" + string.Join("&", batch.Select(id => "id=" + Uri.EscapeDataString(id)));
                var json = await _http.GetStringAsync(url, ct);
                foreach (var (id, rating) in ParseResponse(json))
                    result[id] = rating;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IMDb ratings request failed: {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>
    /// Parses the proxy's response: [{"imdbId":"tt…","rating":9.3,"votes":3252447}, …].
    /// Entries with a null/zero rating are skipped.
    /// </summary>
    public static IEnumerable<(string ImdbId, ImdbRating Rating)> ParseResponse(string json)
    {
        var list = new List<(string, ImdbRating)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("imdbId", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
                if (!el.TryGetProperty("rating", out var rEl) || rEl.ValueKind != JsonValueKind.Number) continue;

                var rating = rEl.GetDouble();
                if (rating <= 0) continue;
                var votes = el.TryGetProperty("votes", out var vEl) && vEl.ValueKind == JsonValueKind.Number
                    ? vEl.GetInt32() : 0;
                list.Add((idEl.GetString()!, new ImdbRating(rating, votes)));
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"IMDb ratings response unreadable: {ex.Message}");
        }
        return list;
    }

    /// <summary>True for strings shaped like an IMDb title id ("tt" + digits).</summary>
    public static bool IsImdbId(string? id)
    {
        var s = id?.Trim();
        return s is { Length: >= 9 } && s.StartsWith("tt", StringComparison.OrdinalIgnoreCase) && s[2..].All(char.IsDigit);
    }
}
