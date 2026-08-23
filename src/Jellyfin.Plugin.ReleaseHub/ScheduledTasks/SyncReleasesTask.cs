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
    private readonly ILogger<SyncReleasesTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncReleasesTask"/> class.
    /// </summary>
    /// <param name="releaseService">The orchestrating service.</param>
    /// <param name="logger">The logger.</param>
    public SyncReleasesTask(ReleaseService releaseService, ILogger<SyncReleasesTask> logger)
    {
        _releaseService = releaseService;
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

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
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
    /// The interval comes from plugin configuration. A configured value of zero means "manual only",
    /// in which case no trigger is registered and the task only runs when someone starts it.
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
    }
}
