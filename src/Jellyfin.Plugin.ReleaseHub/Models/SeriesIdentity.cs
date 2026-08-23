using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ReleaseHub.Models;

/// <summary>
/// Everything ReleaseHub knows about a series before it has been matched to a provider.
/// </summary>
/// <remarks>
/// Built either from a Jellyfin library item or from a followed item. It deliberately says nothing
/// about <em>which</em> metadata plugin populated the identifiers: ReleaseHub consumes whatever
/// Jellyfin exposes and never calls another plugin directly.
/// </remarks>
public sealed class SeriesIdentity
{
    /// <summary>Well-known provider id key for IMDb.</summary>
    public const string Imdb = "Imdb";

    /// <summary>Well-known provider id key for TMDb.</summary>
    public const string Tmdb = "Tmdb";

    /// <summary>Well-known provider id key for TheTVDB.</summary>
    public const string Tvdb = "Tvdb";

    /// <summary>Well-known provider id key for AniDB.</summary>
    public const string AniDb = "AniDB";

    /// <summary>Well-known provider id key for AniList.</summary>
    public const string AniList = "AniList";

    /// <summary>Well-known provider id key for MyAnimeList.</summary>
    public const string MyAnimeList = "MyAnimeList";

    /// <summary>Well-known provider id key for TVMaze.</summary>
    public const string TvMaze = "TvMaze";

    /// <summary>
    /// Gets or sets the Jellyfin item id, when the series exists in the library.
    /// </summary>
    public Guid? JellyfinItemId { get; set; }

    /// <summary>
    /// Gets or sets the primary title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original-language title, when Jellyfin has one.
    /// </summary>
    public string? OriginalTitle { get; set; }

    /// <summary>
    /// Gets or sets the production year, when known.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the provider identifiers Jellyfin already holds for this series.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets a value indicating whether ReleaseHub believes this is anime.
    /// </summary>
    /// <remarks>
    /// Inferred from the presence of anime-specific identifiers and from genre hints, never from the
    /// library's name or from a special Jellyfin media type, because anime libraries are ordinary
    /// TV Series libraries and users must not have to rename them.
    /// </remarks>
    public bool IsAnime { get; set; }

    /// <summary>
    /// Looks up a provider identifier.
    /// </summary>
    /// <param name="key">One of the well-known provider keys on this type.</param>
    /// <returns>The identifier, or <see langword="null"/> when Jellyfin does not have it.</returns>
    public string? GetProviderId(string key)
        => ProviderIds.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
