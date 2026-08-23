using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ReleaseHub.Models;

/// <summary>
/// A stored association between a Jellyfin item and a provider series.
/// </summary>
/// <param name="JellyfinItemId">The Jellyfin item.</param>
/// <param name="Provider">The provider.</param>
/// <param name="ProviderId">The provider's series identifier.</param>
/// <param name="Confidence">How the association was arrived at.</param>
/// <param name="IsManual">Whether a user chose this association explicitly.</param>
/// <param name="UpdatedUtc">When the association was last written.</param>
public sealed record StoredMapping(
    Guid JellyfinItemId,
    ReleaseProviderKind Provider,
    string ProviderId,
    MatchConfidence Confidence,
    bool IsManual,
    DateTime UpdatedUtc);

/// <summary>
/// A library series that could not be matched confidently and is awaiting a decision.
/// </summary>
/// <param name="JellyfinItemId">The Jellyfin item.</param>
/// <param name="Provider">The provider that was searched.</param>
/// <param name="Title">The Jellyfin title.</param>
/// <param name="Year">The Jellyfin year, if known.</param>
/// <param name="Candidates">The candidates that were considered, best first.</param>
/// <param name="UpdatedUtc">When this entry was last refreshed.</param>
public sealed record PendingMatch(
    Guid JellyfinItemId,
    ReleaseProviderKind Provider,
    string Title,
    int? Year,
    IReadOnlyList<ProviderSeries> Candidates,
    DateTime UpdatedUtc);

/// <summary>
/// A series a user follows, whether or not it exists in the Jellyfin library.
/// </summary>
/// <param name="UserId">The Jellyfin user.</param>
/// <param name="Provider">The provider.</param>
/// <param name="ProviderId">The provider's series identifier.</param>
/// <param name="Title">The title, cached so the list renders without a provider call.</param>
/// <param name="IsAnime">Whether this is anime.</param>
/// <param name="PosterUrl">The poster URL, cached for the same reason.</param>
/// <param name="AddedUtc">When the user started following.</param>
/// <remarks>
/// Following is purely a tracking relationship: it never adds anything to the Jellyfin library, creates
/// no files, and downloads no media.
/// </remarks>
public sealed record FollowedItem(
    Guid UserId,
    ReleaseProviderKind Provider,
    string ProviderId,
    string Title,
    bool IsAnime,
    string? PosterUrl,
    DateTime AddedUtc);
