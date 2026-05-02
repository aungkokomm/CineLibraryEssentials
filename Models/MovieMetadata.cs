using System.Text.Json.Serialization;

namespace CineLibraryEssentials.Models;

public class MovieMetadata
{
    [JsonPropertyName("id")]
    public int TmdbId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("vote_average")]
    public double Rating { get; set; }

    [JsonPropertyName("runtime")]
    public int Runtime { get; set; }

    [JsonPropertyName("genres")]
    public List<GenreInfo> Genres { get; set; } = new();

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    [JsonPropertyName("cast")]
    public List<CastMember> Cast { get; set; } = new();

    public int Year => !string.IsNullOrEmpty(ReleaseDate) && DateTime.TryParse(ReleaseDate, out var date)
        ? date.Year
        : 0;
}

public class GenreInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class CastMember
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("character")]
    public string Character { get; set; } = string.Empty;

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }
}

public class TmdbSearchResult
{
    [JsonPropertyName("results")]
    public List<MovieMetadata> Results { get; set; } = new();

    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }
}
