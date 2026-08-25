using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ReleaseHub.Models;
using Jellyfin.Plugin.ReleaseHub.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ReleaseHub.Services;

/// <summary>
/// Coordinates library discovery, provider resolution, caching and the read path the UI uses.
/// </summary>
/// <remarks>
/// This is the only place that knows about more than one provider at a time. Each provider is called
/// inside its own try/catch so that one being down degrades that half of the calendar to cached data
/// instead of failing the whole request.
/// </remarks>
public sealed class ReleaseService
{
    /// <summary>
    /// How far ahead a synchronization looks.
    /// </summary>
    /// <remarks>
    /// A full year, which costs nothing extra for the two providers whose price does not depend on the
    /// window: TVMaze returns a show's entire episode list in one request however wide the window, and
    /// TMDb bills one request per film either way. AnimeSchedule is the exception — it bills one
    /// request per week — so it declares its own shorter <see cref="IReleaseProvider.MaxLookAhead"/>
    /// rather than being asked for weeks whose timetable has not been published yet.
    /// </remarks>
    private static readonly TimeSpan SyncHorizon = TimeSpan.FromDays(365);

    /// <summary>
    /// Gets how many days ahead the cache can possibly hold data for.
    /// </summary>
    /// <remarks>
    /// Published so the interface can stop offering to load more at the point where there is nothing
    /// left to load. Without it the client would have to hardcode this number, and would keep showing
    /// empty pages the moment the horizon changed.
    /// </remarks>
    public static int HorizonDays => (int)SyncHorizon.TotalDays;

    /// <summary>
    /// How far back a synchronization looks, so that something that aired earlier today still shows.
    /// </summary>
    private static readonly TimeSpan SyncLookBack = TimeSpan.FromDays(1);

    private readonly CacheService _cache;
    private readonly LibraryDiscoveryService _library;
    private readonly IReadOnlyList<IReleaseProvider> _providers;
    private readonly ILogger<ReleaseService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReleaseService"/> class.
    /// </summary>
    /// <param name="cache">The persistent cache.</param>
    /// <param name="library">The library reader.</param>
    /// <param name="providers">Every registered provider.</param>
    /// <param name="logger">The logger.</param>
    public ReleaseService(
        CacheService cache,
        LibraryDiscoveryService library,
        IEnumerable<IReleaseProvider> providers,
        ILogger<ReleaseService> logger)
    {
        _cache = cache;
        _library = library;
        _providers = providers.ToList();
        _logger = logger;
    }

    /// <summary>
    /// Refreshes provider data for everything the user could see.
    /// </summary>
    /// <param name="progress">Receives 0..100 progress for Jellyfin's task UI.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A summary of what happened.</returns>
    public async Task<SyncSummary> SynchronizeAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var config = Plugin.Config;
        var now = DateTime.UtcNow;
        var fromUtc = now - SyncLookBack;
        var toUtc = now + SyncHorizon;

        await _cache.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var available = _providers.Where(provider => provider.IsAvailable).ToList();
        if (available.Count == 0)
        {
            _logger.LogInformation("ReleaseHub synchronization skipped: no provider is enabled and configured");
            return new SyncSummary(0, 0, 0, 0, []);
        }

        var targets = await BuildTargetsAsync(cancellationToken).ConfigureAwait(false);
        var budget = new RequestBudget(config.MaxRequestsPerSync);

        var resolved = 0;
        var pending = 0;
        var releaseCount = 0;
        var failures = new List<string>();

        // A provider that rejects the credential will reject it identically for every remaining
        // series, so it is dropped for the rest of the run rather than retried hundreds of times.
        var rejected = new HashSet<ReleaseProviderKind>();

        // Bulk-capable providers are primed once for the whole window; per-series providers are asked
        // series by series below. Doing this first also warms the in-provider week memo.
        var bulk = new Dictionary<ReleaseProviderKind, List<ReleaseItem>>();
        foreach (var provider in available.Where(p => p.SupportsBulkSchedule))
        {
            try
            {
                var items = await provider
                    .GetScheduleAsync(fromUtc, WindowEndFor(provider.MaxLookAhead, fromUtc, toUtc), cancellationToken)
                    .ConfigureAwait(false);
                bulk[provider.Kind] = items.ToList();
                _logger.LogDebug("{Provider} returned {Count} scheduled releases", provider.Kind, items.Count);
            }
            catch (ProviderUnavailableException ex)
            {
                failures.Add($"{provider.Kind}: {ex.Message}");
                _logger.LogWarning("{Provider} schedule fetch failed: {Reason}", provider.Kind, ex.Message);
            }
        }

        for (var index = 0; index < targets.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(100.0 * index / Math.Max(targets.Count, 1));

            var target = targets[index];
            var candidates = ChooseProviders(available, target);

            // Walk the preference order until one provider actually yields something. Stopping at the
            // first failure is what previously made a whole series disappear when its preferred
            // provider was unreachable or rejected its credential.
            foreach (var provider in candidates)
            {
                if (rejected.Contains(provider.Kind))
                {
                    continue;
                }

                try
                {
                    var outcome = await SynchronizeTargetAsync(
                        provider,
                        target,
                        bulk,
                        fromUtc,
                        WindowEndFor(provider.MaxLookAhead, fromUtc, toUtc),
                        budget,
                        cancellationToken).ConfigureAwait(false);

                    resolved += outcome.Resolved ? 1 : 0;
                    pending += outcome.Pending ? 1 : 0;
                    releaseCount += outcome.ReleaseCount;

                    if (outcome.Resolved || outcome.Pending)
                    {
                        break;
                    }
                }
                catch (ProviderUnavailableException ex)
                {
                    // Whatever is already cached for this series stays; the next provider is tried,
                    // and failing that the next run will retry from the top.
                    var failure = $"{provider.Kind}: {ex.Message}";
                    if (!failures.Contains(failure, StringComparer.Ordinal))
                    {
                        failures.Add(failure);
                    }

                    if (ex.IsAuthenticationFailure && rejected.Add(provider.Kind))
                    {
                        _logger.LogWarning(
                            "{Provider} rejected the configured credential; skipping it for the rest of "
                            + "this run. Other providers still cover their series",
                            provider.Kind);
                    }

                    _logger.LogWarning(
                        "{Provider} failed while handling \"{Title}\": {Reason}",
                        provider.Kind,
                        target.Title,
                        ex.Message);
                }
            }

            if (budget.IsExhausted)
            {
                _logger.LogInformation(
                    "ReleaseHub stopped early after {Used} provider requests (configured ceiling). "
                    + "{Remaining} series will be picked up next run",
                    budget.Used,
                    targets.Count - index - 1);
                break;
            }
        }

        progress?.Report(100);
        await _cache.SetLastSyncAsync(DateTime.UtcNow, cancellationToken).ConfigureAwait(false);

        // Kept so the settings page can say *why* a provider contributed nothing. A silent failure is
        // what let a rejected API key look like "this series simply has no upcoming episodes".
        await _cache.SetLastSyncIssuesAsync(failures, cancellationToken).ConfigureAwait(false);

        var summary = new SyncSummary(targets.Count, resolved, pending, releaseCount, failures);
        _logger.LogInformation(
            "ReleaseHub synchronization finished: {Considered} series considered, {Resolved} resolved, "
            + "{Pending} awaiting confirmation, {Releases} releases cached",
            summary.Considered,
            summary.Resolved,
            summary.PendingConfirmation,
            summary.ReleasesCached);

        return summary;
    }

    /// <summary>
    /// Reads the calendar for a window, decorated for one user.
    /// </summary>
    /// <param name="userId">The requesting user.</param>
    /// <param name="fromUtc">Inclusive window start.</param>
    /// <param name="toUtc">Inclusive window end.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Deduplicated releases in chronological order.</returns>
    /// <remarks>
    /// Reads only from the cache, so opening the calendar never triggers provider traffic no matter how
    /// often the page is refreshed.
    /// </remarks>
    public async Task<IReadOnlyList<ReleaseItem>> GetCalendarAsync(
        Guid userId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var releases = await _cache.GetReleasesAsync(fromUtc, toUtc, cancellationToken).ConfigureAwait(false);
        var followed = await _cache.GetFollowedAsync(userId, cancellationToken).ConfigureAwait(false);

        var followedKeys = followed
            .Select(item => LibraryDiscoveryService.MakeKey(item.Provider.ToString(), item.ProviderId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var release in releases)
        {
            release.IsFollowed = followedKeys.Contains(
                LibraryDiscoveryService.MakeKey(release.Provider.ToString(), release.ProviderId));

            // Provider summaries are English-only. Jellyfin's own metadata was fetched in the server's
            // language, so for a series the user owns it is usually already translated.
            if (release.JellyfinItemId is { } id)
            {
                release.Summary = _library.GetOverview(id) ?? release.Summary;
            }
        }

        return Deduplicate(releases);
    }

    /// <summary>
    /// Searches every available provider.
    /// </summary>
    /// <param name="userId">The requesting user, used to flag what they already follow.</param>
    /// <param name="query">The search text.</param>
    /// <param name="includeAnime">Whether to search AnimeSchedule.</param>
    /// <param name="includeTvSeries">Whether to search TVMaze.</param>
    /// <param name="includeMovies">Whether to search TMDb.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Search results annotated with library and follow state.</returns>
    public async Task<IReadOnlyList<DiscoverResult>> SearchAsync(
        Guid userId,
        string query,
        bool includeAnime,
        bool includeTvSeries,
        bool includeMovies,
        CancellationToken cancellationToken)
    {
        var followed = await _cache.GetFollowedAsync(userId, cancellationToken).ConfigureAwait(false);
        var followedKeys = followed
            .Select(item => LibraryDiscoveryService.MakeKey(item.Provider.ToString(), item.ProviderId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var libraryIndex = _library.BuildProviderIdIndex();
        var results = new List<DiscoverResult>();

        foreach (var provider in _providers.Where(p => p.IsAvailable))
        {
            var wanted = provider.Kind switch
            {
                ReleaseProviderKind.AnimeSchedule => includeAnime,
                ReleaseProviderKind.Tmdb => includeMovies,
                _ => includeTvSeries
            };

            if (!wanted)
            {
                continue;
            }

            try
            {
                var found = await provider.SearchAsync(query, 20, cancellationToken).ConfigureAwait(false);

                foreach (var series in found)
                {
                    var jellyfinId = FindInLibrary(libraryIndex, series);

                    // Same reasoning as the calendar: prefer the summary Jellyfin already has, which is
                    // in the server's language, and keep the provider's English text otherwise.
                    if (jellyfinId is { } id)
                    {
                        series.Summary = _library.GetOverview(id) ?? series.Summary;
                    }

                    results.Add(new DiscoverResult(
                        series,
                        jellyfinId,
                        followedKeys.Contains(
                            LibraryDiscoveryService.MakeKey(series.Provider.ToString(), series.ProviderId))));
                }
            }
            catch (ProviderUnavailableException ex)
            {
                // A failing provider must not empty the other provider's results.
                _logger.LogWarning("{Provider} search failed: {Reason}", provider.Kind, ex.Message);
            }
        }

        return results;
    }

    /// <summary>
    /// Follows a series for a user.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="provider">The provider.</param>
    /// <param name="providerId">The provider's series identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the series was found and followed.</returns>
    /// <remarks>
    /// Following only records a tracking preference. It adds nothing to the Jellyfin library, writes no
    /// files, and downloads nothing.
    /// </remarks>
    public async Task<bool> FollowAsync(
        Guid userId,
        ReleaseProviderKind provider,
        string providerId,
        CancellationToken cancellationToken)
    {
        var implementation = _providers.FirstOrDefault(p => p.Kind == provider && p.IsAvailable);
        if (implementation is null)
        {
            return false;
        }

        var series = await implementation.GetSeriesAsync(providerId, cancellationToken).ConfigureAwait(false);
        if (series is null)
        {
            return false;
        }

        await _cache.FollowAsync(userId, series, cancellationToken).ConfigureAwait(false);

        // Fetch this series' releases immediately so the calendar reflects the follow without waiting
        // for the next scheduled run.
        try
        {
            var now = DateTime.UtcNow;
            var from = now - SyncLookBack;
            var releases = await implementation
                .GetReleasesAsync(series, from, WindowEndFor(implementation.MaxLookAhead, from, now + SyncHorizon), cancellationToken)
                .ConfigureAwait(false);

            await _cache.SaveReleasesAsync(provider, providerId, releases, cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderUnavailableException ex)
        {
            // The follow itself succeeded; releases will arrive with the next synchronization.
            _logger.LogWarning(
                "Followed \"{Title}\" but could not fetch its releases yet: {Reason}",
                series.Title,
                ex.Message);
        }

        return true;
    }

    /// <summary>
    /// Trims the synchronization window to what a given provider is worth asking for.
    /// </summary>
    /// <param name="maxLookAhead">
    /// The provider's <see cref="IReleaseProvider.MaxLookAhead"/>, or <see langword="null"/> when the
    /// window costs it nothing.
    /// </param>
    /// <param name="fromUtc">Start of the full window.</param>
    /// <param name="toUtc">End of the full window.</param>
    /// <returns>The end of the window to request from this provider.</returns>
    /// <remarks>
    /// Takes the limit rather than the provider so that it can be exercised without standing up an
    /// implementation of the whole provider contract.
    /// </remarks>
    internal static DateTime WindowEndFor(TimeSpan? maxLookAhead, DateTime fromUtc, DateTime toUtc)
    {
        if (maxLookAhead is not { } limit)
        {
            return toUtc;
        }

        var capped = fromUtc + limit;
        return capped < toUtc ? capped : toUtc;
    }

    /// <summary>
    /// Collapses releases that more than one provider reported.
    /// </summary>
    /// <param name="releases">The raw releases.</param>
    /// <returns>One entry per real broadcast, in chronological order.</returns>
    /// <remarks>
    /// <para>
    /// A show can appear on both providers — anime especially — and the sub/dub variant is what makes
    /// this more than a dictionary lookup. Two rules apply within a single broadcast slot:
    /// </para>
    /// <para>
    /// A subbed and a dubbed airing are genuinely different broadcasts and both are kept, because a
    /// viewer may care about only one of them. A record with no variant information, which is every
    /// TVMaze record since TVMaze does not model sub/dub, is redundant as soon as another provider has
    /// identified the same broadcast precisely, so it is dropped rather than shown twice.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<ReleaseItem> Deduplicate(IReadOnlyList<ReleaseItem> releases)
    {
        ArgumentNullException.ThrowIfNull(releases);

        var broadcasts = new Dictionary<string, List<ReleaseItem>>(StringComparer.Ordinal);

        foreach (var release in releases)
        {
            var key = release.GetDeduplicationKey();

            if (!broadcasts.TryGetValue(key, out var group))
            {
                group = [];
                broadcasts[key] = group;
            }

            group.Add(release);
        }

        var result = new List<ReleaseItem>(broadcasts.Count);

        foreach (var group in broadcasts.Values)
        {
            var known = group.Where(item => item.SubDub != SubDubKind.Unknown).ToList();

            if (known.Count == 0)
            {
                // Nobody identified a variant: keep the single richest record.
                result.Add(group.MaxBy(ScoreOf)!);
                continue;
            }

            // One entry per distinct known variant; the unlabelled duplicates fall away.
            foreach (var variant in known.GroupBy(item => item.SubDub))
            {
                result.Add(variant.MaxBy(ScoreOf)!);
            }
        }

        return result
            .OrderBy(item => item.ReleaseUtc ?? DateTime.MaxValue)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.SubDub)
            .ToList();
    }

    private static int ScoreOf(ReleaseItem item)
    {
        var score = 0;

        if (item.HasReleaseTime)
        {
            score += 4;
        }

        if (item.SubDub != SubDubKind.Unknown)
        {
            score += 2;
        }

        if (item.Certainty == DateCertainty.Exact)
        {
            score += 2;
        }

        if (item.IsInLibrary)
        {
            score += 1;
        }

        return score;
    }

    private async Task<IReadOnlyList<SyncTarget>> BuildTargetsAsync(CancellationToken cancellationToken)
    {
        var targets = new List<SyncTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Only series that can still receive episodes: an ended show cannot produce a calendar entry.
        foreach (var identity in _library.GetSeries(onlyActive: true))
        {
            targets.Add(new SyncTarget(identity, null));
        }

        foreach (var followed in await _cache.GetFollowedAsync(Guid.Empty, cancellationToken).ConfigureAwait(false))
        {
            var key = LibraryDiscoveryService.MakeKey(followed.Provider.ToString(), followed.ProviderId);
            if (!seen.Add(key))
            {
                continue;
            }

            targets.Add(new SyncTarget(
                new SeriesIdentity { Title = followed.Title, IsAnime = followed.IsAnime },
                followed));
        }

        return targets;
    }

    /// <summary>
    /// Orders the providers to try for a target, best first.
    /// </summary>
    /// <param name="available">Providers that are enabled and configured.</param>
    /// <param name="target">The series being synchronized.</param>
    /// <returns>The providers to attempt, in order.</returns>
    /// <remarks>
    /// A list rather than a single choice, because "configured" is not the same as "working". A key
    /// that the provider rejects, an outage, or simply a series that provider has never heard of would
    /// otherwise make the series vanish from the calendar entirely — even when the other provider
    /// holds it and Jellyfin already stores a matching identifier for it. Preference still decides the
    /// order; the fallback only decides what happens when the preferred one produces nothing.
    /// </remarks>
    private static List<IReleaseProvider> ChooseProviders(
        IReadOnlyList<IReleaseProvider> available,
        SyncTarget target)
    {
        // A followed item is bound to the provider it was discovered on: its identifier is meaningless
        // anywhere else, so there is nothing to fall back to.
        if (target.Followed is { } followed)
        {
            var bound = available.FirstOrDefault(provider => provider.Kind == followed.Provider);
            return bound is null ? [] : [bound];
        }

        var tvMaze = available.FirstOrDefault(p => p.Kind == ReleaseProviderKind.TvMaze);
        var animeSchedule = available.FirstOrDefault(p => p.Kind == ReleaseProviderKind.AnimeSchedule);
        var tmdb = available.FirstOrDefault(p => p.Kind == ReleaseProviderKind.Tmdb);

        // A film has exactly one possible answer. There is no fallback here on purpose: neither of the
        // other providers catalogues films, so trying them would waste a request and could only ever
        // produce a wrong match against a same-named series.
        if (target.Identity.IsMovie)
        {
            return tmdb is null ? [] : [tmdb];
        }

        // Anime prefers AnimeSchedule, which carries real airtimes and the sub/dub distinction that
        // TVMaze does not model; TVMaze still covers most anime and is the safety net. TMDb is absent
        // from both lists because it holds films, not broadcast schedules.
        var ordered = target.Identity.IsAnime
            ? new[] { animeSchedule, tvMaze }
            : new[] { tvMaze, animeSchedule };

        return ordered.Where(provider => provider is not null).Select(provider => provider!).ToList();
    }

    private async Task<TargetOutcome> SynchronizeTargetAsync(
        IReleaseProvider provider,
        SyncTarget target,
        Dictionary<ReleaseProviderKind, List<ReleaseItem>> bulk,
        DateTime fromUtc,
        DateTime toUtc,
        RequestBudget budget,
        CancellationToken cancellationToken)
    {
        string providerId;

        if (target.Followed is { } followed)
        {
            providerId = followed.ProviderId;
        }
        else
        {
            var identity = target.Identity;
            var jellyfinId = identity.JellyfinItemId!.Value;

            var stored = await _cache.GetMappingAsync(jellyfinId, provider.Kind, cancellationToken)
                .ConfigureAwait(false);

            if (stored is not null)
            {
                providerId = stored.ProviderId;
            }
            else
            {
                budget.Consume();
                var match = await provider.ResolveAsync(identity, cancellationToken).ConfigureAwait(false);

                if (match is null)
                {
                    return TargetOutcome.Nothing;
                }

                if (!match.IsAutoAcceptable)
                {
                    // Deliberately not used. The series stays off the calendar and goes into the
                    // confirmation queue, so a wrong guess can never masquerade as real schedule data.
                    await _cache.SavePendingMatchAsync(
                        jellyfinId,
                        provider.Kind,
                        identity.Title,
                        identity.Year,
                        [match.Series],
                        cancellationToken).ConfigureAwait(false);

                    _logger.LogDebug(
                        "\"{Title}\" needs confirmation on {Provider} ({Reason})",
                        identity.Title,
                        provider.Kind,
                        match.Reason);

                    return TargetOutcome.NeedsConfirmation;
                }

                await _cache.SaveMappingAsync(jellyfinId, match, isManual: false, cancellationToken)
                    .ConfigureAwait(false);

                providerId = match.Series.ProviderId;
            }
        }

        IReadOnlyList<ReleaseItem> releases;

        if (bulk.TryGetValue(provider.Kind, out var scheduled))
        {
            // Already paid for: filter the window that was fetched once for every series at a time.
            releases = scheduled
                .Where(item => string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else
        {
            budget.Consume();
            var series = new ProviderSeries { Provider = provider.Kind, ProviderId = providerId };
            releases = await provider.GetReleasesAsync(series, fromUtc, toUtc, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var release in releases)
        {
            release.JellyfinItemId = target.Identity.JellyfinItemId;
            release.IsInLibrary = target.Identity.JellyfinItemId is not null;
        }

        await _cache.SaveReleasesAsync(provider.Kind, providerId, releases, cancellationToken)
            .ConfigureAwait(false);

        return new TargetOutcome(true, false, releases.Count);
    }

    private static Guid? FindInLibrary(IReadOnlyDictionary<string, Guid> index, ProviderSeries series)
    {
        foreach (var (key, value) in series.ExternalIds)
        {
            if (index.TryGetValue(LibraryDiscoveryService.MakeKey(key, value), out var id))
            {
                return id;
            }
        }

        return null;
    }

    private sealed record SyncTarget(SeriesIdentity Identity, FollowedItem? Followed)
    {
        public string Title => Followed?.Title ?? Identity.Title;
    }

    private readonly record struct TargetOutcome(bool Resolved, bool Pending, int ReleaseCount)
    {
        public static TargetOutcome Nothing => new(false, false, 0);

        public static TargetOutcome NeedsConfirmation => new(false, true, 0);
    }

    /// <summary>
    /// Enforces the configured ceiling on provider requests per synchronization.
    /// </summary>
    private sealed class RequestBudget(int ceiling)
    {
        public int Used { get; private set; }

        public bool IsExhausted => Used >= ceiling;

        public void Consume() => Used++;
    }
}

/// <summary>
/// What a synchronization run did.
/// </summary>
/// <param name="Considered">How many series were examined.</param>
/// <param name="Resolved">How many are mapped to a provider series.</param>
/// <param name="PendingConfirmation">How many need a human to pick the right match.</param>
/// <param name="ReleasesCached">How many releases were written to the cache.</param>
/// <param name="Failures">Provider failures, already stripped of anything sensitive.</param>
public sealed record SyncSummary(
    int Considered,
    int Resolved,
    int PendingConfirmation,
    int ReleasesCached,
    IReadOnlyList<string> Failures);

/// <summary>
/// A search result together with what the requesting user already has.
/// </summary>
/// <param name="Series">The provider series.</param>
/// <param name="JellyfinItemId">The matching library item, when there is one.</param>
/// <param name="IsFollowed">Whether the user already follows it.</param>
public sealed record DiscoverResult(ProviderSeries Series, Guid? JellyfinItemId, bool IsFollowed);
