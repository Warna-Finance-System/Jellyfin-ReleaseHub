using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ReleaseHub.Providers.Tmdb;

/// <summary>
/// A page of results from a TMDb search or discovery endpoint.
/// </summary>
public sealed class TmdbSearchResponse
{
    /// <summary>Gets or sets the page number.</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>Gets or sets the total number of matches.</summary>
    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }

    /// <summary>Gets or sets the films on this page.</summary>
    [JsonPropertyName("results")]
    public IReadOnlyList<TmdbMovie>? Results { get; set; }
}

/// <summary>
/// A film, as returned by search results and by <c>GET /movie/{id}</c>.
/// </summary>
/// <remarks>
/// Search results carry a subset of these fields — notably no <see cref="BelongsToCollection"/> and
/// no <see cref="ImdbId"/> — so anything that depends on those has to fetch the details first.
/// </remarks>
public sealed class TmdbMovie
{
    /// <summary>Gets or sets the TMDb identifier.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the localized title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the original-language title.</summary>
    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }

    /// <summary>
    /// Gets or sets the release date as <c>yyyy-MM-dd</c>.
    /// </summary>
    /// <remarks>
    /// Frequently an empty string for an announced but unscheduled film, and TMDb never carries a
    /// time of day — a film's release is a date, not a broadcast slot.
    /// </remarks>
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    /// <summary>Gets or sets the localized synopsis.</summary>
    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    /// <summary>Gets or sets the poster path, relative to the image CDN root.</summary>
    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    /// <summary>Gets or sets the backdrop path, relative to the image CDN root.</summary>
    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    /// <summary>
    /// Gets or sets the production status, for example <c>Released</c> or <c>Post Production</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Gets or sets the genres. Present on details, absent from search results.</summary>
    [JsonPropertyName("genres")]
    public IReadOnlyList<TmdbGenre>? Genres { get; set; }

    /// <summary>Gets or sets the genre identifiers. Present on search results instead of names.</summary>
    [JsonPropertyName("genre_ids")]
    public IReadOnlyList<int>? GenreIds { get; set; }

    /// <summary>Gets or sets the IMDb identifier. Details only.</summary>
    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    /// <summary>Gets or sets the saga this film belongs to. Details only, and often null.</summary>
    [JsonPropertyName("belongs_to_collection")]
    public TmdbCollectionRef? BelongsToCollection { get; set; }

    /// <summary>Gets or sets the runtime in minutes.</summary>
    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; }
}

/// <summary>
/// A named genre.
/// </summary>
public sealed class TmdbGenre
{
    /// <summary>Gets or sets the genre identifier.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the localized genre name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// The reference to a saga embedded in a film's details.
/// </summary>
public sealed class TmdbCollectionRef
{
    /// <summary>Gets or sets the collection identifier.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the collection name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the collection poster path.</summary>
    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }
}

/// <summary>
/// A saga and every film in it, from <c>GET /collection/{id}</c>.
/// </summary>
/// <remarks>
/// This is what turns "you own one film of a saga" into "here is the next one": the parts list
/// includes entries that have not been released yet.
/// </remarks>
public sealed class TmdbCollectionDetails
{
    /// <summary>Gets or sets the collection identifier.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the collection name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the films in the collection, released or not.</summary>
    [JsonPropertyName("parts")]
    public IReadOnlyList<TmdbMovie>? Parts { get; set; }
}

/// <summary>
/// The result of <c>GET /find/{external_id}</c>.
/// </summary>
public sealed class TmdbFindResponse
{
    /// <summary>Gets or sets the films matching the external identifier.</summary>
    [JsonPropertyName("movie_results")]
    public IReadOnlyList<TmdbMovie>? MovieResults { get; set; }
}
