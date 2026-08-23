using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ReleaseHub.Models;

/// <summary>
/// A series as described by an external provider, normalized across providers.
/// </summary>
public sealed class ProviderSeries
{
    /// <summary>
    /// Gets or sets the provider this record came from.
    /// </summary>
    public ReleaseProviderKind Provider { get; set; }

    /// <summary>
    /// Gets or sets the provider's identifier, used for every subsequent lookup.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the primary title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original-language title, when known.
    /// </summary>
    public string? OriginalTitle { get; set; }

    /// <summary>
    /// Gets or sets alternate titles used when matching against Jellyfin.
    /// </summary>
    public IReadOnlyList<string> AlternateTitles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the premiere year, when known.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the provider's status string, for example <c>Running</c> or <c>Ended</c>.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the genres reported by the provider.
    /// </summary>
    public IReadOnlyList<string> Genres { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the plain-text summary, with provider markup already stripped.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Gets or sets the poster URL.
    /// </summary>
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Gets or sets the backdrop URL.
    /// </summary>
    public string? BackdropUrl { get; set; }

    /// <summary>
    /// Gets or sets the broadcast network.
    /// </summary>
    public string? Network { get; set; }

    /// <summary>
    /// Gets or sets the streaming platform.
    /// </summary>
    public string? StreamingPlatform { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the provider considers this anime.
    /// </summary>
    public bool IsAnime { get; set; }

    /// <summary>
    /// Gets or sets third-party identifiers the provider exposes, keyed by
    /// <see cref="SeriesIdentity"/>'s well-known provider names.
    /// </summary>
    /// <remarks>
    /// These are what make an <see cref="MatchConfidence.Exact"/> match possible: TVMaze publishes
    /// TheTVDB and IMDb ids, which line up with what Jellyfin's metadata plugins already store.
    /// </remarks>
    public IReadOnlyDictionary<string, string> ExternalIds { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
