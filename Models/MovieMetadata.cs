using System.Text.Json.Serialization;

namespace CineLibraryEssentials.Models;

public class MovieMetadata
{
    [JsonPropertyName("id")]
    public int TmdbId { get; set; }

    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("original_title")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("tagline")]
    public string Tagline { get; set; } = string.Empty;

    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("vote_average")]
    public double Rating { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("runtime")]
    public int Runtime { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("genres")]
    public List<GenreInfo> Genres { get; set; } = new();

    [JsonPropertyName("production_countries")]
    public List<CountryInfo> ProductionCountries { get; set; } = new();

    [JsonPropertyName("production_companies")]
    public List<StudioInfo> ProductionCompanies { get; set; } = new();

    [JsonPropertyName("belongs_to_collection")]
    public CollectionInfo? BelongsToCollection { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    // ---- Populated by client-side parsing of append_to_response sub-objects ----

    public List<CastMember> Cast { get; set; } = new();

    /// <summary>Directors extracted from credits.crew where job == "Director".</summary>
    public List<CrewMember> Directors { get; set; } = new();

    /// <summary>Writers extracted from credits.crew where department == "Writing".</summary>
    public List<CrewMember> Writers { get; set; } = new();

    /// <summary>Producers extracted from credits.crew where department == "Production".</summary>
    public List<CrewMember> Producers { get; set; } = new();

    /// <summary>MPAA certification (e.g. "PG-13") for the US region, or empty.</summary>
    public string Certification { get; set; } = string.Empty;

    /// <summary>YouTube trailer URL, or empty if no official trailer published.</summary>
    public string TrailerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Edition tag parsed from the source filename (e.g. "Director's Cut",
    /// "Extended", "IMAX", "4K Remaster"). Written to NFO as &lt;edition&gt;.
    /// </summary>
    public string Edition { get; set; } = string.Empty;

    public int Year => !string.IsNullOrEmpty(ReleaseDate) && DateTime.TryParse(ReleaseDate, out var date)
        ? date.Year
        : 0;
}

// ─────────────────────────────────────────────────────────────────────────────
//  TV-show metadata — separate top-level model so it doesn't have to fight
//  MovieMetadata's movie-specific properties (release_date, runtime, etc.).
//  The Kodi tvshow.nfo and episodedetails .nfo formats expect these fields.
// ─────────────────────────────────────────────────────────────────────────────

public class TvShowMetadata
{
    [JsonPropertyName("id")]
    public int TmdbId { get; set; }

    /// <summary>Populated externally from /tv/{id}/external_ids.</summary>
    public string? ImdbId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("original_name")]
    public string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("tagline")]
    public string Tagline { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("first_air_date")]
    public string FirstAirDate { get; set; } = string.Empty;

    [JsonPropertyName("vote_average")]
    public double Rating { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    /// <summary>Typical episode runtime in minutes (TMDb returns an array; we average it).</summary>
    public int EpisodeRunTime { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("genres")]
    public List<GenreInfo> Genres { get; set; } = new();

    [JsonPropertyName("production_countries")]
    public List<CountryInfo> ProductionCountries { get; set; } = new();

    [JsonPropertyName("production_companies")]
    public List<StudioInfo> ProductionCompanies { get; set; } = new();

    [JsonPropertyName("networks")]
    public List<StudioInfo> Networks { get; set; } = new();

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    /// <summary>Cast list parsed from credits.cast (sorted by billing order).</summary>
    public List<CastMember> Cast { get; set; } = new();

    /// <summary>Crew creators / writers / etc. — Kodi tvshow.nfo writes them as &lt;credits&gt;.</summary>
    public List<CrewMember> Creators { get; set; } = new();

    /// <summary>Content rating ("TV-MA", "PG-13"). Falls back to first non-empty region.</summary>
    public string ContentRating { get; set; } = string.Empty;

    public int Year => !string.IsNullOrEmpty(FirstAirDate) && DateTime.TryParse(FirstAirDate, out var d)
        ? d.Year : 0;
}

public class TvEpisodeMetadata
{
    [JsonPropertyName("id")]
    public int TmdbId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("episode_number")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("air_date")]
    public string AirDate { get; set; } = string.Empty;

    [JsonPropertyName("runtime")]
    public int Runtime { get; set; }

    [JsonPropertyName("vote_average")]
    public double Rating { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    /// <summary>TMDb path to the episode thumbnail (e.g. "/abc.jpg"). Empty if none.</summary>
    [JsonPropertyName("still_path")]
    public string? StillPath { get; set; }
}

public class GenreInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class CountryInfo
{
    [JsonPropertyName("iso_3166_1")]
    public string IsoCode { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class StudioInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class CollectionInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;
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

    /// <summary>Billing order as reported by TMDb (lower = higher billing).</summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }
}

public class CrewMember
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("job")]
    public string Job { get; set; } = string.Empty;

    [JsonPropertyName("department")]
    public string Department { get; set; } = string.Empty;

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
