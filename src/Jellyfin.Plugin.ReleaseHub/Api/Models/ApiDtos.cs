using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.ReleaseHub.Models;
using Jellyfin.Plugin.ReleaseHub.Services;

namespace Jellyfin.Plugin.ReleaseHub.Api.Models;

/// <summary>
/// A release, shaped for the frontend.
/// </summary>
/// <remarks>
/// Separate from <see cref="ReleaseItem"/> so that provider-facing details and internal scoring stay on
/// the server, and so that changing a provider mapping cannot silently change the client contract.
/// </remarks>
public sealed class ReleaseItemDto
{
    /// <summary>Gets or sets the provider name.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider's series identifier.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Gets or sets the matching Jellyfin item id, when the series is in the library.</summary>
    public Guid? JellyfinItemId { get; set; }

    /// <summary>Gets or sets the display title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the original-language title.</summary>
    public string? OriginalTitle { get; set; }

    /// <summary>Gets or sets a value indicating whether this is anime.</summary>
    public bool IsAnime { get; set; }

    /// <summary>Gets or sets a value indicating whether the series is in the library.</summary>
    public bool IsInLibrary { get; set; }

    /// <summary>Gets or sets a value indicating whether the user follows the series.</summary>
    public bool IsFollowed { get; set; }

    /// <summary>Gets or sets the season number, when known.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number, when known.</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>Gets or sets the episode title, when known.</summary>
    public string? EpisodeTitle { get; set; }

    /// <summary>Gets or sets the release instant in UTC, as ISO-8601.</summary>
    public DateTime? ReleaseUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="ReleaseUtc"/> carries a real broadcast time.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/> the client must render the date alone. Providers commonly supply a
    /// placeholder time for date-only entries, and showing it would invent a broadcast time.
    /// </remarks>
    public bool HasReleaseTime { get; set; }

    /// <summary>
    /// Gets or sets how much the date can be trusted: <c>Unknown</c>, <c>AnnouncedNoDate</c>,
    /// <c>Approximate</c> or <c>Exact</c>.
    /// </summary>
    public string Certainty { get; set; } = nameof(DateCertainty.Unknown);

    /// <summary>Gets or sets a human-readable approximation such as a delay note.</summary>
    public string? ApproximateLabel { get; set; }

    /// <summary>Gets or sets the sub/dub variant: <c>Unknown</c>, <c>Sub</c>, <c>Dub</c> or <c>Raw</c>.</summary>
    public string SubDub { get; set; } = nameof(SubDubKind.Unknown);

    /// <summary>Gets or sets the broadcast network.</summary>
    public string? Network { get; set; }

    /// <summary>Gets or sets the streaming platform.</summary>
    public string? StreamingPlatform { get; set; }

    /// <summary>Gets or sets the poster URL.</summary>
    public string? PosterUrl { get; set; }

    /// <summary>Gets or sets the plain-text summary.</summary>
    public string? Summary { get; set; }

    /// <summary>Gets or sets the provider's series status.</summary>
    public string? Status { get; set; }

    /// <summary>
    /// Creates a transport object from a normalized release.
    /// </summary>
    /// <param name="item">The release.</param>
    /// <returns>The transport object.</returns>
    public static ReleaseItemDto From(ReleaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new ReleaseItemDto
        {
            Provider = item.Provider.ToString(),
            ProviderId = item.ProviderId,
            JellyfinItemId = item.JellyfinItemId,
            Title = item.Title,
            OriginalTitle = item.OriginalTitle,
            IsAnime = item.IsAnime,
            IsInLibrary = item.IsInLibrary,
            IsFollowed = item.IsFollowed,
            SeasonNumber = item.SeasonNumber,
            EpisodeNumber = item.EpisodeNumber,
            EpisodeTitle = item.EpisodeTitle,
            ReleaseUtc = item.ReleaseUtc,
            HasReleaseTime = item.HasReleaseTime,
            Certainty = item.Certainty.ToString(),
            ApproximateLabel = item.ApproximateLabel,
            SubDub = item.SubDub.ToString(),
            Network = item.Network,
            StreamingPlatform = item.StreamingPlatform,

            // Prefer Jellyfin's own artwork for anything already in the library: it is already on disk,
            // already sized for the client, and avoids fetching a duplicate from the provider.
            PosterUrl = item.JellyfinItemId is { } id
                ? string.Create(CultureInfo.InvariantCulture, $"Items/{id:N}/Images/Primary")
                : item.PosterUrl,
            Summary = item.Summary,
            Status = item.Status
        };
    }
}

/// <summary>
/// A calendar or upcoming response.
/// </summary>
public sealed class CalendarResponseDto
{
    /// <summary>Gets or sets the inclusive window start.</summary>
    public DateTime FromUtc { get; set; }

    /// <summary>Gets or sets the inclusive window end.</summary>
    public DateTime ToUtc { get; set; }

    /// <summary>Gets or sets the window length in days.</summary>
    public int RangeDays { get; set; }

    /// <summary>
    /// Gets or sets when provider data was last refreshed, or <see langword="null"/> if never.
    /// </summary>
    /// <remarks>
    /// Shown by the client so that stale data is visibly stale while a provider is unreachable, rather
    /// than looking current.
    /// </remarks>
    public DateTime? LastSyncUtc { get; set; }

    /// <summary>Gets or sets the releases, in chronological order.</summary>
    public IReadOnlyList<ReleaseItemDto> Items { get; set; } = [];
}

/// <summary>
/// A discover search result.
/// </summary>
public sealed class DiscoverResultDto
{
    /// <summary>Gets or sets the provider name.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider's series identifier.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the original-language title.</summary>
    public string? OriginalTitle { get; set; }

    /// <summary>Gets or sets the premiere year.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets the provider's status string.</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the genres.</summary>
    public IReadOnlyList<string> Genres { get; set; } = [];

    /// <summary>Gets or sets the plain-text summary.</summary>
    public string? Summary { get; set; }

    /// <summary>Gets or sets the poster URL.</summary>
    public string? PosterUrl { get; set; }

    /// <summary>Gets or sets the broadcast network.</summary>
    public string? Network { get; set; }

    /// <summary>Gets or sets the streaming platform.</summary>
    public string? StreamingPlatform { get; set; }

    /// <summary>Gets or sets a value indicating whether this is anime.</summary>
    public bool IsAnime { get; set; }

    /// <summary>Gets or sets a value indicating whether the series is already in the library.</summary>
    public bool IsInLibrary { get; set; }

    /// <summary>Gets or sets a value indicating whether the user already follows it.</summary>
    public bool IsFollowed { get; set; }

    /// <summary>
    /// Creates a transport object from a search result.
    /// </summary>
    /// <param name="result">The search result.</param>
    /// <returns>The transport object.</returns>
    public static DiscoverResultDto From(DiscoverResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var series = result.Series;

        return new DiscoverResultDto
        {
            Provider = series.Provider.ToString(),
            ProviderId = series.ProviderId,
            Title = series.Title,
            OriginalTitle = series.OriginalTitle,
            Year = series.Year,
            Status = series.Status,
            Genres = series.Genres,
            Summary = series.Summary,
            PosterUrl = result.JellyfinItemId is { } id
                ? string.Create(CultureInfo.InvariantCulture, $"Items/{id:N}/Images/Primary")
                : series.PosterUrl,
            Network = series.Network,
            StreamingPlatform = series.StreamingPlatform,
            IsAnime = series.IsAnime,
            IsInLibrary = result.JellyfinItemId is not null,
            IsFollowed = result.IsFollowed
        };
    }
}

/// <summary>
/// A followed series, shaped for the frontend.
/// </summary>
public sealed class FollowedItemDto
{
    /// <summary>Gets or sets the provider name.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider's series identifier.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether this is anime.</summary>
    public bool IsAnime { get; set; }

    /// <summary>Gets or sets the poster URL.</summary>
    public string? PosterUrl { get; set; }

    /// <summary>Gets or sets when the user started following.</summary>
    public DateTime AddedUtc { get; set; }

    /// <summary>
    /// Creates a transport object from a stored follow.
    /// </summary>
    /// <param name="item">The stored follow.</param>
    /// <returns>The transport object.</returns>
    public static FollowedItemDto From(FollowedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new FollowedItemDto
        {
            Provider = item.Provider.ToString(),
            ProviderId = item.ProviderId,
            Title = item.Title,
            IsAnime = item.IsAnime,
            PosterUrl = item.PosterUrl,
            AddedUtc = item.AddedUtc
        };
    }
}

/// <summary>
/// Identifies a series to follow or unfollow.
/// </summary>
public sealed class FollowRequestDto
{
    /// <summary>Gets or sets the provider.</summary>
    public ReleaseProviderKind Provider { get; set; }

    /// <summary>Gets or sets the provider's series identifier.</summary>
    public string ProviderId { get; set; } = string.Empty;
}

/// <summary>
/// Confirms which provider series a library item corresponds to.
/// </summary>
public sealed class ConfirmMatchRequestDto
{
    /// <summary>Gets or sets the Jellyfin item.</summary>
    public Guid JellyfinItemId { get; set; }

    /// <summary>Gets or sets the provider.</summary>
    public ReleaseProviderKind Provider { get; set; }

    /// <summary>Gets or sets the provider's series identifier.</summary>
    public string ProviderId { get; set; } = string.Empty;
}
