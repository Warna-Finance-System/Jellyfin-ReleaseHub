using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ReleaseHub.Api.Models;
using Jellyfin.Plugin.ReleaseHub.Integration;
using Jellyfin.Plugin.ReleaseHub.Models;
using Jellyfin.Plugin.ReleaseHub.Providers;
using Jellyfin.Plugin.ReleaseHub.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ReleaseHub.Api;

/// <summary>
/// Public API surface of the ReleaseHub plugin.
/// </summary>
/// <remarks>
/// <para>
/// A bare <see cref="AuthorizeAttribute"/> is deliberate: Jellyfin (10.11 and 12 alike) defines no
/// "default" named policy, and configures ASP.NET Core's <c>DefaultPolicy</c> to require its custom
/// authentication scheme plus <c>DefaultAuthorizationRequirement</c> — which is exactly "any
/// authenticated, non-restricted user". Naming a policy that does not exist throws at request time rather than at
/// startup, so administrator-only actions use real constants from <see cref="Policies"/>.
/// </para>
/// <para>
/// Nothing returned here ever contains a provider credential. Static assets are served separately by
/// Jellyfin's own unauthenticated plugin page endpoint.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("ReleaseHub")]
[Produces("application/json")]
public class ReleaseHubController : ControllerBase
{
    /// <summary>
    /// The claim jellyfin-web's token carries the acting user's id in.
    /// </summary>
    private const string UserIdClaim = "Jellyfin-UserId";

    private readonly ReleaseService _releaseService;
    private readonly CacheService _cache;
    private readonly ITaskManager _taskManager;
    private readonly IReadOnlyList<IReleaseProvider> _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReleaseHubController"/> class.
    /// </summary>
    /// <param name="releaseService">The orchestrating service.</param>
    /// <param name="cache">The persistent cache.</param>
    /// <param name="taskManager">Jellyfin's scheduled task manager.</param>
    /// <param name="providers">The registered providers.</param>
    public ReleaseHubController(
        ReleaseService releaseService,
        CacheService cache,
        ITaskManager taskManager,
        IEnumerable<IReleaseProvider> providers)
    {
        _releaseService = releaseService;
        _cache = cache;
        _taskManager = taskManager;
        _providers = providers.ToList();
    }

    /// <summary>
    /// Gets the current ReleaseHub status and the non-secret parts of its configuration.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Status returned.</response>
    /// <returns>The current <see cref="ReleaseHubStatusDto"/>.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReleaseHubStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        var config = Plugin.Config;
        var hasAnimeKey = !string.IsNullOrWhiteSpace(config.AnimeScheduleApiKey);

        return new ReleaseHubStatusDto
        {
            Version = Plugin.Instance?.Version.ToString(3) ?? "0.0.0",

            // A boolean, never the key itself.
            AnimeScheduleApiKeyConfigured = hasAnimeKey,
            UserTabAvailable = FileTransformationHook.IsUserTabAvailable,
            TvMazeAvailable = config.TvMazeEnabled && config.EnableTvSeries,
            AnimeScheduleAvailable = config.AnimeScheduleEnabled && config.EnableAnime && hasAnimeKey,
            TmdbAvailable = config.TmdbEnabled && config.EnableMovies
                && !string.IsNullOrWhiteSpace(config.TmdbApiKey),
            TmdbApiKeyConfigured = !string.IsNullOrWhiteSpace(config.TmdbApiKey),
            MoviesEnabled = config.EnableMovies,
            AnimeEnabled = config.EnableAnime,
            TvSeriesEnabled = config.EnableTvSeries,
            DefaultCalendarRangeDays = config.DefaultCalendarRangeDays,
            HorizonDays = ReleaseService.HorizonDays,
            Language = config.Language,
            LastSyncUtc = await _cache.GetLastSyncAsync(cancellationToken).ConfigureAwait(false),
            LastSyncIssues = await _cache.GetLastSyncIssuesAsync(cancellationToken).ConfigureAwait(false)
        };
    }

    /// <summary>
    /// Gets the release calendar.
    /// </summary>
    /// <param name="days">How many days to include. Clamped to 1..90.</param>
    /// <param name="offset">
    /// Days to shift the window by, relative to today. Used by the week view to page forward.
    /// </param>
    /// <param name="filter">One of <c>all</c>, <c>anime</c> or <c>tv</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Calendar returned.</response>
    /// <returns>The releases, in chronological order.</returns>
    /// <remarks>
    /// Served entirely from cache, so repeatedly opening the calendar causes no provider traffic.
    /// </remarks>
    [HttpGet("Calendar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CalendarResponseDto>> GetCalendar(
        [FromQuery] int days,
        [FromQuery] int offset,
        [FromQuery] string? filter,
        CancellationToken cancellationToken)
    {
        var range = days <= 0 ? Plugin.Config.DefaultCalendarRangeDays : days;
        range = Math.Clamp(range, 1, 90);

        // Bounded to the window a synchronization actually fills, so paging past the cached horizon
        // returns an honest empty week instead of pretending nothing airs.
        offset = Math.Clamp(offset, -7, ReleaseService.HorizonDays);

        // Start from midnight UTC so that "today" is complete rather than starting at the current hour.
        var fromUtc = DateTime.UtcNow.Date.AddDays(offset);
        var toUtc = fromUtc.AddDays(range).AddTicks(-1);

        var releases = await _releaseService
            .GetCalendarAsync(GetUserId(), fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);

        return new CalendarResponseDto
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            RangeDays = range,
            LastSyncUtc = await _cache.GetLastSyncAsync(cancellationToken).ConfigureAwait(false),
            Items = Filter(releases, filter).Select(ReleaseItemDto.From).ToList()
        };
    }

    /// <summary>
    /// Gets the next releases as a flat, compact list.
    /// </summary>
    /// <param name="days">
    /// How many days of window to include. Defaults to 60 and is capped at what remains of the
    /// synchronization horizon after <paramref name="offset"/>.
    /// </param>
    /// <param name="offset">
    /// How many days after today the window starts. Lets the client fetch only the slice it does not
    /// already have instead of re-requesting everything it is already showing.
    /// </param>
    /// <param name="filter">One of <c>all</c>, <c>anime</c>, <c>tv</c> or <c>movie</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Upcoming releases returned.</response>
    /// <returns>The releases, in chronological order.</returns>
    /// <remarks>
    /// Returned flat rather than pre-grouped into Today/Tomorrow/This week/Later: those buckets depend
    /// on the viewer's own timezone, which only the browser knows.
    ///
    /// Widening the window costs nothing beyond a larger SQLite read — this path never contacts a
    /// provider — but it is still capped at the horizon a synchronization actually fills, because
    /// beyond that an empty answer would mean "not looked up", not "nothing scheduled".
    /// </remarks>
    [HttpGet("Upcoming")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CalendarResponseDto>> GetUpcoming(
        [FromQuery] int days,
        [FromQuery] int offset,
        [FromQuery] string? filter,
        CancellationToken cancellationToken)
    {
        var horizon = ReleaseService.HorizonDays;

        // One day short of the horizon, so that a window of at least one day always remains: a start
        // exactly on the horizon would otherwise clamp the range to zero.
        var start = Math.Clamp(offset, 0, horizon - 1);
        var range = Math.Clamp(days <= 0 ? 60 : days, 1, horizon - start);

        var fromUtc = DateTime.UtcNow.Date.AddDays(start);
        var toUtc = fromUtc.AddDays(range).AddTicks(-1);

        var releases = await _releaseService
            .GetCalendarAsync(GetUserId(), fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);

        return new CalendarResponseDto
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            RangeDays = range,
            LastSyncUtc = await _cache.GetLastSyncAsync(cancellationToken).ConfigureAwait(false),
            Items = Filter(releases, filter).Select(ReleaseItemDto.From).ToList()
        };
    }

    /// <summary>
    /// Searches the providers for series to follow.
    /// </summary>
    /// <param name="q">The search text.</param>
    /// <param name="filter">One of <c>all</c>, <c>anime</c> or <c>tv</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Results returned.</response>
    /// <returns>The search results.</returns>
    [HttpGet("Discover/Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DiscoverResultDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] string? filter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return new List<DiscoverResultDto>();
        }

        var config = Plugin.Config;
        var scoped = !string.IsNullOrWhiteSpace(filter)
            && !string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase);

        bool Wanted(string kind) =>
            !scoped || string.Equals(filter, kind, StringComparison.OrdinalIgnoreCase);

        var results = await _releaseService
            .SearchAsync(
                GetUserId(),
                q,
                config.EnableAnime && Wanted("anime"),
                config.EnableTvSeries && Wanted("tv"),
                config.EnableMovies && Wanted("movie"),
                cancellationToken)
            .ConfigureAwait(false);

        return results.Select(DiscoverResultDto.From).ToList();
    }

    /// <summary>
    /// Lists the series the current user follows.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Followed series returned.</response>
    /// <returns>The followed series.</returns>
    [HttpGet("Following")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FollowedItemDto>>> GetFollowing(
        CancellationToken cancellationToken)
    {
        var followed = await _cache.GetFollowedAsync(GetUserId(), cancellationToken).ConfigureAwait(false);
        return followed.Select(FollowedItemDto.From).ToList();
    }

    /// <summary>
    /// Follows a series for the current user.
    /// </summary>
    /// <param name="request">Which series to follow.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="204">The series is now followed.</response>
    /// <response code="404">The provider does not know this series, or is unavailable.</response>
    /// <returns>No content.</returns>
    /// <remarks>
    /// Following records a tracking preference only. It never adds anything to the Jellyfin library,
    /// creates no files and downloads nothing.
    /// </remarks>
    [HttpPost("Follow")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Follow(
        [FromBody] FollowRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var followed = await _releaseService
            .FollowAsync(GetUserId(), request.Provider, request.ProviderId, cancellationToken)
            .ConfigureAwait(false);

        return followed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Stops following a series for the current user.
    /// </summary>
    /// <param name="request">Which series to unfollow.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="204">The series is no longer followed.</response>
    /// <returns>No content.</returns>
    [HttpPost("Unfollow")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Unfollow(
        [FromBody] FollowRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _cache.UnfollowAsync(GetUserId(), request.Provider, request.ProviderId, cancellationToken)
            .ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Tests connectivity to a provider.
    /// </summary>
    /// <param name="provider">The provider name, <c>TvMaze</c> or <c>AnimeSchedule</c>.</param>
    /// <param name="request">
    /// Optionally carries a candidate credential to test instead of the stored one.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The test ran; inspect the result for success.</response>
    /// <response code="404">No such provider.</response>
    /// <returns>The test outcome.</returns>
    /// <remarks>
    /// Administrator-only because it exercises the configured credential. The response says whether the
    /// credential worked, never what it is.
    /// </remarks>
    [HttpPost("Providers/{provider}/Test")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProviderTestResult>> TestProvider(
        string provider,
        [FromBody] ProviderTestRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ReleaseProviderKind>(provider, ignoreCase: true, out var kind))
        {
            return NotFound();
        }

        var implementation = _providers.FirstOrDefault(candidate => candidate.Kind == kind);
        if (implementation is null)
        {
            return NotFound();
        }

        // The candidate credential is used for this request only and never written to configuration.
        return await implementation
            .TestConnectionAsync(request?.ApiKey, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Lists library series that could not be matched confidently.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Pending matches returned.</response>
    /// <returns>The pending matches.</returns>
    /// <remarks>
    /// These series are deliberately absent from the calendar. Showing them here is what keeps a weak
    /// match from being either silently dropped or wrongly trusted.
    /// </remarks>
    [HttpGet("Matches/Pending")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PendingMatch>>> GetPendingMatches(
        CancellationToken cancellationToken)
    {
        var pending = await _cache.GetPendingMatchesAsync(cancellationToken).ConfigureAwait(false);
        return Ok(pending);
    }

    /// <summary>
    /// Confirms which provider series a library item corresponds to.
    /// </summary>
    /// <param name="request">The chosen mapping.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="204">The mapping was stored.</response>
    /// <response code="404">The provider does not know this series.</response>
    /// <returns>No content.</returns>
    /// <remarks>
    /// A mapping confirmed here is marked manual, and no later automatic resolution will overwrite it.
    /// </remarks>
    [HttpPost("Matches/Confirm")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ConfirmMatch(
        [FromBody] ConfirmMatchRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var implementation = _providers.FirstOrDefault(
            candidate => candidate.Kind == request.Provider && candidate.IsAvailable);

        if (implementation is null)
        {
            return NotFound();
        }

        var series = await implementation.GetSeriesAsync(request.ProviderId, cancellationToken)
            .ConfigureAwait(false);

        if (series is null)
        {
            return NotFound();
        }

        await _cache.SaveMappingAsync(
            request.JellyfinItemId,
            new ProviderMatch(series, MatchConfidence.Exact, "confirmed by an administrator"),
            isManual: true,
            cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Reports what ReleaseHub observes about the web interface it is served alongside.
    /// </summary>
    /// <response code="200">The observations were returned.</response>
    /// <returns>The current state.</returns>
    /// <remarks>
    /// Administrator-only: it describes server-side integration state, not anything a viewer needs.
    /// </remarks>
    [HttpGet("Interface/Status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<InterfaceStatusDto> GetInterfaceStatus()
    {
        return new InterfaceStatusDto
        {
            FileTransformationAvailable = Integration.FileTransformationHook.IsUserTabAvailable,
            IndexHtmlObserved = Integration.TransformationPatches.IndexHtmlObserved,
            AbyssSpotlightDetected = Integration.TransformationPatches.AbyssSpotlightDetected,
            AbyssSpotlightRemoved = Integration.TransformationPatches.AbyssSpotlightRemoved,
            HideAbyssSpotlight = Plugin.Config.HideAbyssSpotlight
        };
    }

    /// <summary>
    /// Starts a synchronization immediately.
    /// </summary>
    /// <response code="204">The task was queued.</response>
    /// <returns>No content.</returns>
    /// <remarks>
    /// Queued through Jellyfin's task manager rather than run inline, so it shows up in the dashboard's
    /// scheduled task UI with progress and cancellation like any other task. Flagged as deliberate
    /// first: the task skips a run whose data is still fresh, which is right for a startup trigger and
    /// wrong for a button someone just pressed.
    /// </remarks>
    [HttpPost("Sync")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Synchronize()
    {
        ScheduledTasks.SyncReleasesTask.RequestImmediateRun();
        _taskManager.QueueScheduledTask<ScheduledTasks.SyncReleasesTask>();
        return NoContent();
    }

    private static IEnumerable<ReleaseItem> Filter(IEnumerable<ReleaseItem> releases, string? filter)
    {
        if (string.Equals(filter, "anime", StringComparison.OrdinalIgnoreCase))
        {
            return releases.Where(item => item.IsAnime);
        }

        if (string.Equals(filter, "movie", StringComparison.OrdinalIgnoreCase))
        {
            return releases.Where(item => item.Kind == ReleaseKind.Movie);
        }

        // "TV series" now means neither anime nor a film, rather than simply "not anime".
        if (string.Equals(filter, "tv", StringComparison.OrdinalIgnoreCase))
        {
            return releases.Where(item => !item.IsAnime && item.Kind != ReleaseKind.Movie);
        }

        return releases;
    }

    /// <summary>
    /// Reads the acting user from the request's claims.
    /// </summary>
    /// <returns>The user id, or <see cref="Guid.Empty"/> when the token carries none.</returns>
    /// <remarks>
    /// Jellyfin's own <c>ClaimsPrincipal.GetUserId()</c> extension lives in <c>Jellyfin.Api</c>, which is
    /// not published for plugins to reference, so the claim is read directly. An API key token has no
    /// user, which yields <see cref="Guid.Empty"/> and therefore an empty personal follow list rather
    /// than someone else's.
    /// </remarks>
    private Guid GetUserId()
    {
        var value = User.FindFirstValue(UserIdClaim);
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
