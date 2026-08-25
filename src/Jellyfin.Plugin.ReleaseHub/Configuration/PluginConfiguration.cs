using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ReleaseHub.Configuration;

/// <summary>
/// Persisted configuration for the ReleaseHub plugin.
/// </summary>
/// <remarks>
/// Jellyfin serializes this type to
/// <c>&lt;data&gt;/plugins/configurations/Jellyfin.Plugin.ReleaseHub.xml</c>. It therefore holds the
/// AnimeSchedule API key at rest. The key must never be returned by the plugin's public API, never be
/// written to a log, and never reach the browser: see
/// <c>Api.Models.ReleaseHubSettingsDto</c> for the redacted shape that is sent to clients.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// The TVMaze public API root used when the administrator has not overridden it.
    /// </summary>
    public const string DefaultTvMazeBaseUrl = "https://api.tvmaze.com";

    /// <summary>
    /// The AnimeSchedule v3 API root used when the administrator has not overridden it.
    /// </summary>
    public const string DefaultAnimeScheduleBaseUrl = "https://animeschedule.net/api/v3";

    /// <summary>
    /// The TMDb v3 API root used when the administrator has not overridden it.
    /// </summary>
    public const string DefaultTmdbBaseUrl = "https://api.themoviedb.org/3";

    /// <summary>
    /// The TMDb image CDN root used when the administrator has not overridden it.
    /// </summary>
    /// <remarks>
    /// TMDb returns bare paths such as <c>/abc.jpg</c>; a size segment is appended to this root to
    /// build a usable URL. Configurable because TMDb publishes the current roots through its
    /// <c>/configuration</c> endpoint and reserves the right to change them.
    /// </remarks>
    public const string DefaultTmdbImageBaseUrl = "https://image.tmdb.org/t/p/w500";

    /// <summary>
    /// Gets or sets the UI language override. An empty value means "follow the Jellyfin client".
    /// </summary>
    /// <remarks>
    /// When empty, the frontend resolves the culture from <c>document.documentElement</c>'s
    /// <c>data-culture</c> attribute and falls back to <c>navigator.language</c>, matching how
    /// jellyfin-web itself picks a locale.
    /// </remarks>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of days the calendar shows by default. Expected values are 7, 14 or 30.
    /// </summary>
    public int DefaultCalendarRangeDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets a value indicating whether anime releases are collected and shown.
    /// </summary>
    public bool EnableAnime { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether TV series releases are collected and shown.
    /// </summary>
    public bool EnableTvSeries { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the TVMaze provider is enabled.
    /// </summary>
    public bool TvMazeEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the TVMaze API base URL.
    /// </summary>
    /// <remarks>
    /// Configurable purely for forward compatibility if TVMaze relocates its endpoint. TVMaze requires
    /// no API key for normal public use, so no key field exists for this provider.
    /// </remarks>
    public string TvMazeBaseUrl { get; set; } = DefaultTvMazeBaseUrl;

    /// <summary>
    /// Gets or sets a value indicating whether the AnimeSchedule provider is enabled.
    /// </summary>
    public bool AnimeScheduleEnabled { get; set; }

    /// <summary>
    /// Gets or sets the AnimeSchedule API base URL.
    /// </summary>
    public string AnimeScheduleBaseUrl { get; set; } = DefaultAnimeScheduleBaseUrl;

    /// <summary>
    /// Gets or sets the AnimeSchedule bearer token.
    /// </summary>
    /// <remarks>
    /// Never expose this value outside the server process. AnimeSchedule's terms explicitly prohibit
    /// exposing tokens, so all AnimeSchedule requests are made by the backend on the user's behalf.
    /// </remarks>
    public string AnimeScheduleApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether movie releases are collected and shown.
    /// </summary>
    public bool EnableMovies { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the TMDb provider is enabled.
    /// </summary>
    public bool TmdbEnabled { get; set; }

    /// <summary>
    /// Gets or sets the TMDb API base URL.
    /// </summary>
    public string TmdbBaseUrl { get; set; } = DefaultTmdbBaseUrl;

    /// <summary>
    /// Gets or sets the TMDb image CDN base URL, including the size segment.
    /// </summary>
    public string TmdbImageBaseUrl { get; set; } = DefaultTmdbImageBaseUrl;

    /// <summary>
    /// Gets or sets the TMDb API read access token.
    /// </summary>
    /// <remarks>
    /// This is the v4 "API Read Access Token" from a TMDb account's API settings, sent as a bearer
    /// token — not the older v3 key that travels in the query string. Handled like every other
    /// credential here: server-side only, never logged, never returned by ReleaseHub's own API.
    /// </remarks>
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether ReleaseHub follows film collections.
    /// </summary>
    /// <remarks>
    /// When enabled, owning one film of a saga surfaces that saga's unreleased entries. This is the
    /// only place ReleaseHub looks beyond the exact items in the library, so it is separately
    /// switchable.
    /// </remarks>
    public bool TrackMovieCollections { get; set; } = true;

    /// <summary>
    /// Gets or sets how often the synchronization task runs, in hours. Zero means manual only.
    /// </summary>
    public int RefreshIntervalHours { get; set; } = 6;

    /// <summary>
    /// Gets or sets how long resolved series metadata stays fresh, in hours.
    /// </summary>
    public int SeriesCacheHours { get; set; } = 24;

    /// <summary>
    /// Gets or sets how long upcoming release data stays fresh, in hours.
    /// </summary>
    public int ReleaseCacheHours { get; set; } = 6;

    /// <summary>
    /// Gets or sets the ceiling on outbound provider requests per synchronization run.
    /// </summary>
    /// <remarks>
    /// A safety net on top of the per-provider rate limiter: it bounds how much a single sync can cost
    /// even when a very large library would otherwise justify thousands of lookups.
    /// </remarks>
    public int MaxRequestsPerSync { get; set; } = 500;

    /// <summary>
    /// Gets or sets a value indicating whether recently viewed provider images are cached on disk.
    /// </summary>
    public bool EnableImageCache { get; set; } = true;

    /// <summary>
    /// Gets or sets the image cache ceiling in megabytes.
    /// </summary>
    public int ImageCacheSizeLimitMb { get; set; } = 128;

    /// <summary>
    /// Gets or sets how many days a cached image survives without being requested.
    /// </summary>
    public int ImageCacheExpirationDays { get; set; } = 14;

    /// <summary>
    /// Gets or sets a value indicating whether the Abyss theme's spotlight carousel is hidden.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Abyss installs by writing into jellyfin-web on disk, including a
    /// <c>&lt;script src="ui/spotlight-loader.js" data-abyss-spotlight&gt;</c> tag in <c>index.html</c>
    /// that mounts a second hero carousel at the top of the home tab. On a server that also runs the
    /// Media Bar plugin the two stack, and the one underneath shows through as ghost text, a stray
    /// info button and a second row of pagination dots.
    /// </para>
    /// <para>
    /// Setting this hides that carousel by stripping the tag from the served document rather than from
    /// the file, which is what makes the choice survive an Abyss update, an Abyss reinstall or a
    /// Jellyfin upgrade — each of which rewrites the file and would undo an edit made to it. Nothing
    /// Abyss installed is modified or deleted, so clearing this brings the carousel straight back.
    /// </para>
    /// </remarks>
    public bool HideAbyssSpotlight { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether verbose diagnostics are written to the Jellyfin log.
    /// </summary>
    /// <remarks>
    /// Debug logging never includes credentials; provider URLs are redacted before being logged.
    /// </remarks>
    public bool EnableDebugLogging { get; set; }
}
