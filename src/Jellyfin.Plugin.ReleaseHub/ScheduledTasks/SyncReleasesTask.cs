using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ReleaseHub.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ReleaseHub.ScheduledTasks;

/// <summary>
/// Refreshes release data from the configured providers.
/// </summary>
public sealed class SyncReleasesTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ReleaseService _releaseService;
    private readonly CacheService _cache;
    private readonly ILogger<SyncReleasesTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncReleasesTask"/> class.
    /// </summary>
    /// <param name="releaseService">The orchestrating service.</param>
    /// <param name="cache">The persistent cache, consulted to skip redundant runs.</param>
    /// <param name="logger">The logger.</param>
    public SyncReleasesTask(ReleaseService releaseService, CacheService cache, ILogger<SyncReleasesTask> logger)
    {
        _releaseService = releaseService;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "ReleaseHub - Synchronize Releases";

    /// <inheritdoc />
    public string Key => "ReleaseHubSyncReleases";

    /// <inheritdoc />
    public string Description =>
        "Resolves library series against TVMaze and AnimeSchedule and caches their upcoming releases. "
        + "Reads the library only; never modifies media or metadata.";

    /// <inheritdoc />
    public string Category => "ReleaseHub";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <summary>
    /// How recently a run must have completed for the next one to be considered redundant.
    /// </summary>
    /// <remarks>
    /// Short on purpose. It exists to absorb bursts of server restarts, not to override the
    /// configured refresh interval, and it must never make a deliberate "Synchronize now" feel
    /// ignored — so anything older than this always runs.
    /// </remarks>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Set when a person asked for this run, cleared by the run that honours it.
    /// </summary>
    /// <remarks>
    /// <see cref="IScheduledTask"/> gives the task no way to tell a trigger apart from someone
    /// pressing "Synchronize now", and the guard above must not apply to the second: a button that
    /// silently does nothing is worse than one that costs a few requests. Static because the task
    /// manager owns the instance it runs, so the request has nowhere else to live.
    /// </remarks>
    private static int _requestedRun;

    /// <summary>
    /// Marks the next execution as deliberate, so the freshness guard lets it through.
    /// </summary>
    public static void RequestImmediateRun() => Interlocked.Exchange(ref _requestedRun, 1);

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var requested = Interlocked.Exchange(ref _requestedRun, 0) == 1;

        var lastSync = await _cache.GetLastSyncAsync(cancellationToken).ConfigureAwait(false);
        if (!requested && lastSync is { } previous && DateTime.UtcNow - previous < MinimumInterval)
        {
            _logger.LogInformation(
                "ReleaseHub synchronization skipped: data was refreshed {Minutes:0} minute(s) ago",
                (DateTime.UtcNow - previous).TotalMinutes);

            progress?.Report(100);
            return;
        }

        var summary = await _releaseService.SynchronizeAsync(progress, cancellationToken).ConfigureAwait(false);

        foreach (var failure in summary.Failures)
        {
            // Surfaced at warning level so a persistently broken provider is visible in the dashboard
            // log without failing the task — cached data is still being served.
            _logger.LogWarning("ReleaseHub provider issue during synchronization: {Failure}", failure);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The interval comes from plugin configuration. A configured value of zero means "manual only",
    /// in which case neither trigger is registered and the task only runs when someone starts it.
    /// </para>
    /// <para>
    /// A startup trigger accompanies the interval because an interval trigger is re-armed from zero
    /// every time the server starts. On a machine that restarts more often than the configured
    /// interval — a desktop server, or one being actively worked on — the interval alone would never
    /// fire, and the calendar would stay empty with no indication why.
    /// <see cref="ExecuteAsync"/> keeps that cheap by skipping a run whose data is still fresh.
    /// </para>
    /// </remarks>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var hours = Plugin.Config.RefreshIntervalHours;
        if (hours <= 0)
        {
            yield break;
        }

        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(hours).Ticks
        };

        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };
    }
}
