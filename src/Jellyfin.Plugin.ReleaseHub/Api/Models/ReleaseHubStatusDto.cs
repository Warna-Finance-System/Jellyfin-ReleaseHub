using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ReleaseHub.Api.Models;

/// <summary>
/// The client-facing view of ReleaseHub's current state.
/// </summary>
/// <remarks>
/// This type is the security boundary for the plugin configuration: it deliberately exposes only
/// whether an AnimeSchedule key exists, never the key itself. Nothing that reaches a browser may
/// carry provider credentials.
/// </remarks>
public sealed class ReleaseHubStatusDto
{
    /// <summary>
    /// Gets or sets the plugin version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether an AnimeSchedule API key is stored on the server.
    /// </summary>
    public bool AnimeScheduleApiKeyConfigured { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether ordinary users get a ReleaseHub entry in the main menu.
    /// </summary>
    public bool UserTabAvailable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the TVMaze provider is usable.
    /// </summary>
    public bool TvMazeAvailable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the AnimeSchedule provider is usable.
    /// </summary>
    /// <remarks>Requires the provider to be enabled <em>and</em> a key to be present.</remarks>
    public bool AnimeScheduleAvailable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the TMDb provider is usable.
    /// </summary>
    public bool TmdbAvailable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a TMDb token is stored on the server.
    /// </summary>
    public bool TmdbApiKeyConfigured { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether film content is shown.
    /// </summary>
    public bool MoviesEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether anime content is shown.
    /// </summary>
    public bool AnimeEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether TV series content is shown.
    /// </summary>
    public bool TvSeriesEnabled { get; set; }

    /// <summary>
    /// Gets or sets the default number of days shown by the calendar.
    /// </summary>
    public int DefaultCalendarRangeDays { get; set; }

    /// <summary>
    /// Gets or sets how many days ahead the cache can hold data for.
    /// </summary>
    /// <remarks>
    /// The client stops offering "load more" here. Past this point a synchronization has never fetched
    /// anything, so an empty result would say "nothing is scheduled" when it means "nothing was looked
    /// up".
    /// </remarks>
    public int HorizonDays { get; set; }

    /// <summary>
    /// Gets or sets the configured UI language, or an empty string to follow the client.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when release data was last refreshed, or <see langword="null"/> if never.
    /// </summary>
    public DateTime? LastSyncUtc { get; set; }

    /// <summary>
    /// Gets or sets the provider problems seen during the last synchronization.
    /// </summary>
    /// <remarks>
    /// Surfaced so that a rejected credential or an outage reads as a provider problem rather than as
    /// "nothing is airing". Messages carry a status code, never a credential.
    /// </remarks>
    public IReadOnlyList<string> LastSyncIssues { get; set; } = [];
}
