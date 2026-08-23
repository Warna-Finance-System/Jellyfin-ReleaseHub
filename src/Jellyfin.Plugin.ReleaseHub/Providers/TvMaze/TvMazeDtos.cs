using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ReleaseHub.Providers.TvMaze;

/// <summary>
/// One entry of a TVMaze <c>/search/shows</c> response.
/// </summary>
public sealed class TvMazeSearchResult
{
    /// <summary>Gets or sets TVMaze's relevance score.</summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>Gets or sets the matched show.</summary>
    [JsonPropertyName("show")]
    public TvMazeShow? Show { get; set; }
}

/// <summary>
/// A TVMaze show.
/// </summary>
public sealed class TvMazeShow
{
    /// <summary>Gets or sets the TVMaze identifier.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the show name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the show type, for example <c>Scripted</c> or <c>Animation</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Gets or sets the original language.</summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    /// <summary>Gets or sets the genres.</summary>
    [JsonPropertyName("genres")]
    public IReadOnlyList<string>? Genres { get; set; }

    /// <summary>Gets or sets the status, for example <c>Running</c> or <c>Ended</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Gets or sets the premiere date as <c>yyyy-MM-dd</c>.</summary>
    [JsonPropertyName("premiered")]
    public string? Premiered { get; set; }

    /// <summary>Gets or sets the broadcast network.</summary>
    [JsonPropertyName("network")]
    public TvMazeNetwork? Network { get; set; }

    /// <summary>Gets or sets the streaming channel.</summary>
    [JsonPropertyName("webChannel")]
    public TvMazeNetwork? WebChannel { get; set; }

    /// <summary>Gets or sets identifiers on other databases.</summary>
    [JsonPropertyName("externals")]
    public TvMazeExternals? Externals { get; set; }

    /// <summary>Gets or sets the images.</summary>
    [JsonPropertyName("image")]
    public TvMazeImage? Image { get; set; }

    /// <summary>Gets or sets the HTML summary.</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>Gets or sets embedded sub-resources requested with <c>?embed=</c>.</summary>
    [JsonPropertyName("_embedded")]
    public TvMazeEmbedded? Embedded { get; set; }
}

/// <summary>
/// A TVMaze network or web channel.
/// </summary>
public sealed class TvMazeNetwork
{
    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Identifiers TVMaze holds for other databases.
/// </summary>
/// <remarks>
/// These are what allow an exact match against the identifiers Jellyfin's metadata plugins already
/// wrote, without ReleaseHub caring which plugin produced them.
/// </remarks>
public sealed class TvMazeExternals
{
    /// <summary>Gets or sets the TheTVDB identifier.</summary>
    [JsonPropertyName("thetvdb")]
    public int? TheTvDb { get; set; }

    /// <summary>Gets or sets the IMDb identifier.</summary>
    [JsonPropertyName("imdb")]
    public string? Imdb { get; set; }

    /// <summary>Gets or sets the (legacy) TVRage identifier.</summary>
    [JsonPropertyName("tvrage")]
    public int? TvRage { get; set; }
}

/// <summary>
/// A TVMaze image pair.
/// </summary>
public sealed class TvMazeImage
{
    /// <summary>Gets or sets the medium-resolution URL.</summary>
    [JsonPropertyName("medium")]
    public string? Medium { get; set; }

    /// <summary>Gets or sets the full-resolution URL.</summary>
    [JsonPropertyName("original")]
    public string? Original { get; set; }
}

/// <summary>
/// Sub-resources embedded in a show response.
/// </summary>
public sealed class TvMazeEmbedded
{
    /// <summary>Gets or sets the episode list.</summary>
    [JsonPropertyName("episodes")]
    public IReadOnlyList<TvMazeEpisode>? Episodes { get; set; }
}

/// <summary>
/// A TVMaze episode.
/// </summary>
public sealed class TvMazeEpisode
{
    /// <summary>Gets or sets the episode identifier.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the episode title.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the season number.</summary>
    [JsonPropertyName("season")]
    public int? Season { get; set; }

    /// <summary>Gets or sets the episode number within the season.</summary>
    [JsonPropertyName("number")]
    public int? Number { get; set; }

    /// <summary>Gets or sets the episode type, for example <c>regular</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Gets or sets the air date as <c>yyyy-MM-dd</c>.</summary>
    [JsonPropertyName("airdate")]
    public string? AirDate { get; set; }

    /// <summary>
    /// Gets or sets the local air time as <c>HH:mm</c>, or an empty string when TVMaze does not know it.
    /// </summary>
    /// <remarks>
    /// This field, not <see cref="AirStamp"/>, decides whether a time may be displayed: when TVMaze has
    /// no airtime it still emits an <see cref="AirStamp"/>, synthesised at 12:00 UTC. Rendering that
    /// would be inventing a broadcast time.
    /// </remarks>
    [JsonPropertyName("airtime")]
    public string? AirTime { get; set; }

    /// <summary>Gets or sets the absolute air instant, including offset.</summary>
    [JsonPropertyName("airstamp")]
    public DateTimeOffset? AirStamp { get; set; }

    /// <summary>Gets or sets the HTML summary.</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }
}
