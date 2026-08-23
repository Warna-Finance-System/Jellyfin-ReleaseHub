using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ReleaseHub.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ReleaseHub.ScheduledTasks;

/// <summary>
/// Empties ReleaseHub's cached provider data.
/// </summary>
/// <remarks>
/// Follows and hand-corrected matches are user decisions rather than cached data, so this task leaves
/// them intact. It exists to recover from stale or bad provider data, not to reset the plugin.
/// </remarks>
public sealed class ClearCacheTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly CacheService _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClearCacheTask"/> class.
    /// </summary>
    /// <param name="cache">The cache.</param>
    public ClearCacheTask(CacheService cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public string Name => "ReleaseHub - Clear Cache";

    /// <inheritdoc />
    public string Key => "ReleaseHubClearCache";

    /// <inheritdoc />
    public string Description =>
        "Clears cached release and series data. Followed series and manually corrected matches are kept.";

    /// <inheritdoc />
    public string Category => "ReleaseHub";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        progress.Report(0);
        var task = _cache.ClearAsync(includeFollows: false, cancellationToken);
        progress.Report(100);
        return task;
    }

    /// <inheritdoc />
    /// <remarks>Destructive, so it never runs on a schedule of its own.</remarks>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
