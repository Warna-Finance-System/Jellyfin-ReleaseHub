using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ReleaseHub.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ReleaseHub.Services;

/// <summary>
/// Reads the Jellyfin library and turns each series into something ReleaseHub can resolve.
/// </summary>
/// <remarks>
/// <para>
/// Strictly read-only. Nothing in this class writes to an item, saves metadata, touches a file, or asks
/// Jellyfin to refresh anything — ReleaseHub is a tracking plugin and must leave libraries exactly as it
/// found them.
/// </para>
/// <para>
/// It also never calls another metadata plugin. Whatever identifiers Anime Multi Source, AniDB, TMDb,
/// TVDB or anything else wrote are simply read back out of Jellyfin, which is what keeps ReleaseHub
/// working across different metadata setups.
/// </para>
/// </remarks>
public sealed class LibraryDiscoveryService
{
    /// <summary>
    /// Jellyfin provider-id keys that only ever appear on anime, whatever plugin wrote them.
    /// </summary>
    private static readonly string[] AnimeIdentifierKeys =
    [
        SeriesIdentity.AniDb,
        SeriesIdentity.AniList,
        SeriesIdentity.MyAnimeList,
        "Kitsu",
        "AniSearch",
        "AnimePlanet"
    ];

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryDiscoveryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryDiscoveryService"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin's library manager.</param>
    /// <param name="logger">The logger.</param>
    public LibraryDiscoveryService(ILibraryManager libraryManager, ILogger<LibraryDiscoveryService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Lists every series in the library as a resolvable identity.
    /// </summary>
    /// <param name="onlyActive">
    /// When <see langword="true"/>, series Jellyfin marks as ended are skipped.
    /// </param>
    /// <returns>The series identities.</returns>
    /// <remarks>
    /// Ended series receive no further episodes, so resolving them on every synchronization would spend
    /// the request budget on shows that cannot produce a calendar entry.
    /// </remarks>
    public IReadOnlyList<SeriesIdentity> GetSeries(bool onlyActive)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series],
            Recursive = true,

            // Virtual and missing entries are placeholders Jellyfin creates for absent episodes; they
            // are not things the user actually has, and matching them would double-count series.
            IsVirtualItem = false
        };

        var items = _libraryManager.GetItemList(query);
        var results = new List<SeriesIdentity>(items.Count);

        foreach (var item in items)
        {
            if (item is not Series series)
            {
                continue;
            }

            if (onlyActive && series.Status == MediaBrowser.Model.Entities.SeriesStatus.Ended)
            {
                continue;
            }

            results.Add(ToIdentity(series));
        }

        _logger.LogDebug(
            "ReleaseHub found {Count} series in the library (onlyActive: {OnlyActive})",
            results.Count,
            onlyActive);

        return results;
    }

    /// <summary>
    /// Builds a lookup from provider identifier to Jellyfin item id.
    /// </summary>
    /// <returns>
    /// A dictionary keyed by <c>"{provider}:{id}"</c>, used to flag search results as already owned.
    /// </returns>
    public IReadOnlyDictionary<string, Guid> BuildProviderIdIndex()
    {
        var index = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var identity in GetSeries(onlyActive: false))
        {
            if (identity.JellyfinItemId is not { } id)
            {
                continue;
            }

            foreach (var (key, value) in identity.ProviderIds)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    index[MakeKey(key, value)] = id;
                }
            }
        }

        return index;
    }

    /// <summary>
    /// Reads the summary Jellyfin already holds for a library item.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item.</param>
    /// <returns>The stored overview, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// Neither provider serves localized text, so a TVMaze or AnimeSchedule summary is always English.
    /// Jellyfin's own metadata, however, was fetched in the server's configured language — so for a
    /// series the user actually owns, this is usually the summary in their language. Callers fall back
    /// to the provider's English text when it is missing, which is the same principle already applied
    /// to artwork: prefer what Jellyfin has, fetch from the provider only when it has nothing.
    /// </remarks>
    public string? GetOverview(Guid jellyfinItemId)
    {
        if (jellyfinItemId == Guid.Empty)
        {
            return null;
        }

        var item = _libraryManager.GetItemById(jellyfinItemId);
        return string.IsNullOrWhiteSpace(item?.Overview) ? null : item.Overview;
    }

    /// <summary>
    /// Builds the key used by <see cref="BuildProviderIdIndex"/>.
    /// </summary>
    /// <param name="providerKey">The provider id key, for example <c>Tvdb</c>.</param>
    /// <param name="value">The identifier value.</param>
    /// <returns>The composite key.</returns>
    public static string MakeKey(string providerKey, string value)
        => string.Create(CultureInfo.InvariantCulture, $"{providerKey}:{value}");

    /// <summary>
    /// Converts a Jellyfin series into a provider-agnostic identity.
    /// </summary>
    /// <param name="series">The Jellyfin series.</param>
    /// <returns>The identity.</returns>
    internal static SeriesIdentity ToIdentity(Series series)
    {
        ArgumentNullException.ThrowIfNull(series);

        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in series.ProviderIds)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                providerIds[key] = value;
            }
        }

        return new SeriesIdentity
        {
            JellyfinItemId = series.Id,
            Title = series.Name ?? string.Empty,
            OriginalTitle = series.OriginalTitle,
            Year = series.ProductionYear ?? series.PremiereDate?.Year,
            ProviderIds = providerIds,
            IsAnime = LooksLikeAnime(providerIds, series.Genres, series.Tags)
        };
    }

    /// <summary>
    /// Decides whether a library series should be treated as anime.
    /// </summary>
    /// <param name="providerIds">The identifiers Jellyfin holds.</param>
    /// <param name="genres">The series genres.</param>
    /// <param name="tags">The series tags.</param>
    /// <returns><see langword="true"/> when the series looks like anime.</returns>
    /// <remarks>
    /// Anime libraries in Jellyfin are ordinary TV Series libraries, so this must never depend on how a
    /// library is named or typed — users are not going to rename their folders for a plugin. The strong
    /// signal is an anime-only identifier such as AniDB or AniList; genres and tags are the fallback.
    /// </remarks>
    internal static bool LooksLikeAnime(
        IReadOnlyDictionary<string, string> providerIds,
        IReadOnlyList<string>? genres,
        IReadOnlyList<string>? tags)
    {
        ArgumentNullException.ThrowIfNull(providerIds);

        foreach (var key in AnimeIdentifierKeys)
        {
            if (providerIds.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        return ContainsAnime(genres) || ContainsAnime(tags);
    }

    private static bool ContainsAnime(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return false;
        }

        foreach (var value in values)
        {
            if (string.Equals(value, "Anime", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
