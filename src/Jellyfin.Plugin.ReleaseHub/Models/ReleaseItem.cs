using System;

namespace Jellyfin.Plugin.ReleaseHub.Models;

/// <summary>
/// A single normalized release, whatever provider it came from.
/// </summary>
/// <remarks>
/// This is the only release shape the API and the frontend ever see. Provider DTOs stay inside their
/// own provider namespace so that swapping or adding a provider never ripples into the UI.
/// </remarks>
public sealed class ReleaseItem
{
    /// <summary>
    /// Gets or sets the provider that produced this record.
    /// </summary>
    public ReleaseProviderKind Provider { get; set; }

    /// <summary>
    /// Gets or sets the provider's own identifier for the series.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the matching Jellyfin item id, or <see langword="null"/> when not in the library.
    /// </summary>
    public Guid? JellyfinItemId { get; set; }

    /// <summary>
    /// Gets or sets the display title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original-language title, when the provider exposes one.
    /// </summary>
    public string? OriginalTitle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is anime rather than a standard TV series.
    /// </summary>
    public bool IsAnime { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the series exists in the user's Jellyfin library.
    /// </summary>
    public bool IsInLibrary { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the requesting user follows this series.
    /// </summary>
    public bool IsFollowed { get; set; }

    /// <summary>
    /// Gets or sets what kind of release this is.
    /// </summary>
    public ReleaseKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the season number, when known.
    /// </summary>
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode number, when known.
    /// </summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode title, when known.
    /// </summary>
    public string? EpisodeTitle { get; set; }

    /// <summary>
    /// Gets or sets the release instant in UTC.
    /// </summary>
    /// <remarks>
    /// Null whenever <see cref="Certainty"/> is <see cref="DateCertainty.Unknown"/> or
    /// <see cref="DateCertainty.AnnouncedNoDate"/>. A date is never invented to fill this in.
    /// </remarks>
    public DateTime? ReleaseUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="ReleaseUtc"/> carries a meaningful time of day.
    /// </summary>
    /// <remarks>
    /// Providers routinely give a date with no airtime. When this is <see langword="false"/> the UI shows
    /// the date alone rather than rendering a misleading midnight.
    /// </remarks>
    public bool HasReleaseTime { get; set; }

    /// <summary>
    /// Gets or sets the provider's original timezone identifier, for display and debugging.
    /// </summary>
    public string? ReleaseTimezone { get; set; }

    /// <summary>
    /// Gets or sets how much the date can be trusted.
    /// </summary>
    public DateCertainty Certainty { get; set; }

    /// <summary>
    /// Gets or sets a human-readable approximation such as "Fall 2027", used when no exact date exists.
    /// </summary>
    public string? ApproximateLabel { get; set; }

    /// <summary>
    /// Gets or sets the sub/dub variant for anime releases.
    /// </summary>
    public SubDubKind SubDub { get; set; }

    /// <summary>
    /// Gets or sets the broadcast network, when known.
    /// </summary>
    public string? Network { get; set; }

    /// <summary>
    /// Gets or sets the streaming platform, when known.
    /// </summary>
    public string? StreamingPlatform { get; set; }

    /// <summary>
    /// Gets or sets the poster URL.
    /// </summary>
    /// <remarks>
    /// For items already in the library this points at Jellyfin's own image endpoint so no duplicate is
    /// fetched from the provider; for everything else it is the provider's remote URL.
    /// </remarks>
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Gets or sets the backdrop URL, when available.
    /// </summary>
    public string? BackdropUrl { get; set; }

    /// <summary>
    /// Gets or sets the plain-text summary.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Gets or sets the series status as reported by the provider, for example <c>Running</c>.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets how confident the resolver was when linking this to a Jellyfin item.
    /// </summary>
    public MatchConfidence Confidence { get; set; }

    /// <summary>
    /// Gets or sets when this record was last refreshed from the provider.
    /// </summary>
    public DateTime LastUpdatedUtc { get; set; }

    /// <summary>
    /// Builds the key identifying the broadcast this record describes, ignoring the sub/dub variant.
    /// </summary>
    /// <returns>A stable key for this episode on this date.</returns>
    /// <remarks>
    /// <para>
    /// Deliberately keyed on the show plus the episode coordinates rather than on the provider, so that
    /// the same episode reported by both TVMaze and AnimeSchedule collapses into one calendar entry.
    /// </para>
    /// <para>
    /// The variant is excluded on purpose. TVMaze does not model sub/dub at all, so including it here
    /// would make a TVMaze record and an AnimeSchedule record for the same broadcast look like two
    /// different things. Splitting genuinely distinct variants apart is handled separately, once the
    /// records for one broadcast have been gathered.
    /// </para>
    /// </remarks>
    public string GetDeduplicationKey()
    {
        var show = JellyfinItemId is { } id
            ? "jf:" + id.ToString("N")
            : "t:" + Title.Trim().ToLowerInvariant();

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{show}|{SeasonNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}|{EpisodeNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}|{ReleaseUtc?.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "-"}");
    }

    /// <summary>
    /// Builds the key that identifies this record uniquely within the cache.
    /// </summary>
    /// <returns>A stable per-record key.</returns>
    /// <remarks>
    /// Unlike <see cref="GetDeduplicationKey"/> this one keeps the sub/dub variant, because a subbed
    /// and a dubbed airing of the same episode are two rows that must both survive being written.
    /// Sharing one key between storage and display would silently discard one of them.
    /// </remarks>
    public string GetCacheKey()
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{GetDeduplicationKey()}|{SubDub}");
}
