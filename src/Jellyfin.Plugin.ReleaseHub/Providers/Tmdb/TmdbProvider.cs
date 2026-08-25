using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ReleaseHub.Models;
using Jellyfin.Plugin.ReleaseHub.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ReleaseHub.Providers.Tmdb;

/// <summary>
/// Release provider backed by TMDb, covering films and the sagas they belong to.
/// </summary>
/// <remarks>
/// <para>
/// Films differ from broadcasts in a way that shapes this whole class: TMDb gives a release date and
/// never a time, and that date is frequently provisional, or absent entirely for an announced film.
/// Everything here therefore maps onto <see cref="DateCertainty"/> honestly rather than producing a
/// timestamp that would look like a broadcast schedule.
/// </para>
/// <para>
/// Requires a TMDb v4 API Read Access Token, sent as a bearer token. ReleaseHub is not endorsed by or
/// affiliated with TMDb.
/// </para>
/// </remarks>
public sealed class TmdbProvider : IReleaseProvider, IDisposable
{
    /// <summary>
    /// TMDb replaced its old published ceiling of 40 requests per 10 seconds with server-side
    /// throttling and no documented figure. ReleaseHub still paces itself rather than relying on the
    /// other side to say no.
    /// </summary>
    private const int PermitsPerWindow = 20;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly ProviderHttpClient _http;
    private readonly ILogger<TmdbProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin's HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public TmdbProvider(IHttpClientFactory httpClientFactory, ILogger<TmdbProvider> logger)
    {
        _logger = logger;
        _http = new ProviderHttpClient(httpClientFactory, logger, "TMDb", PermitsPerWindow, Window);
    }

    /// <inheritdoc />
    public ReleaseProviderKind Kind => ReleaseProviderKind.Tmdb;

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            var config = Plugin.Config;
            return config.TmdbEnabled
                && config.EnableMovies
                && !string.IsNullOrWhiteSpace(config.TmdbApiKey);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// TMDb's <c>/movie/upcoming</c> lists cinema releases worldwide, which is not what the calendar
    /// is for: it tracks what the user owns or follows. The per-title path is both cheaper and more
    /// relevant.
    /// </remarks>
    public bool SupportsBulkSchedule => false;

    /// <inheritdoc />
    /// <remarks>
    /// One request per film, whatever the window: a wider horizon reads further into an answer already
    /// paid for. Films are also announced years ahead, which is exactly what a long horizon is for.
    /// </remarks>
    public TimeSpan? MaxLookAhead => null;

    /// <inheritdoc />
    public Task<IReadOnlyList<ReleaseItem>> GetScheduleAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ReleaseItem>>(Array.Empty<ReleaseItem>());

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderSeries>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ProviderSeries>();
        }

        var url = BuildUrl("search/movie", ("query", query), ("include_adult", "false"));
        var page = await _http
            .GetJsonAsync<TmdbSearchResponse>(url, Authorize, cancellationToken)
            .ConfigureAwait(false);

        if (page?.Results is null)
        {
            return Array.Empty<ProviderSeries>();
        }

        return page.Results.Take(limit).Select(movie => Map(movie, ImageRoot())).ToList();
    }

    /// <inheritdoc />
    public async Task<ProviderSeries?> GetSeriesAsync(string providerId, CancellationToken cancellationToken)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        var movie = await GetMovieAsync(providerId, cancellationToken).ConfigureAwait(false);
        return movie is null ? null : Map(movie, ImageRoot());
    }

    /// <inheritdoc />
    public async Task<ProviderMatch?> ResolveAsync(SeriesIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (!IsAvailable)
        {
            return null;
        }

        // A TMDb id straight from Jellyfin settles it outright, and Jellyfin's film metadata comes
        // from TMDb far more often than not, so this is the usual path rather than the lucky one.
        var tmdbId = identity.GetProviderId(SeriesIdentity.Tmdb);
        if (tmdbId is not null)
        {
            var direct = await GetMovieAsync(tmdbId, cancellationToken).ConfigureAwait(false);
            if (direct is not null)
            {
                return new ProviderMatch(Map(direct, ImageRoot()), MatchConfidence.Exact, "TMDb id from Jellyfin");
            }
        }

        var imdbId = identity.GetProviderId(SeriesIdentity.Imdb);
        if (imdbId is not null)
        {
            var url = BuildUrl("find/" + Uri.EscapeDataString(imdbId), ("external_source", "imdb_id"));
            var found = await _http
                .GetJsonAsync<TmdbFindResponse>(url, Authorize, cancellationToken)
                .ConfigureAwait(false);

            if (found?.MovieResults is { Count: > 0 } movies)
            {
                return new ProviderMatch(Map(movies[0], ImageRoot()), MatchConfidence.Exact, "IMDb id match");
            }
        }

        var candidates = await SearchAsync(identity.Title, 10, cancellationToken).ConfigureAwait(false);
        return SeriesMatcher.PickBest(identity, candidates);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns the film itself when it has not been released yet, plus any unreleased entry of the
    /// saga it belongs to. That second part is the point of this provider: owning one film of a series
    /// is how someone learns the next one has been dated.
    /// </remarks>
    public async Task<IReadOnlyList<ReleaseItem>> GetReleasesAsync(
        ProviderSeries series,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(series);

        if (!IsAvailable)
        {
            return Array.Empty<ReleaseItem>();
        }

        var movie = await GetMovieAsync(series.ProviderId, cancellationToken).ConfigureAwait(false);
        if (movie is null)
        {
            return Array.Empty<ReleaseItem>();
        }

        var now = DateTime.UtcNow;
        var releases = new List<ReleaseItem>();

        var own = MapRelease(movie, now, ImageRoot());
        if (own is not null && InWindow(own, fromUtc, toUtc))
        {
            releases.Add(own);
        }

        if (!Plugin.Config.TrackMovieCollections || movie.BelongsToCollection is not { } saga)
        {
            return releases;
        }

        try
        {
            var url = BuildUrl("collection/" + saga.Id.ToString(CultureInfo.InvariantCulture));
            var details = await _http
                .GetJsonAsync<TmdbCollectionDetails>(url, Authorize, cancellationToken)
                .ConfigureAwait(false);

            foreach (var part in details?.Parts ?? [])
            {
                // The film we started from is already handled above.
                if (part.Id == movie.Id)
                {
                    continue;
                }

                var release = MapRelease(part, now, ImageRoot());
                if (release is not null && InWindow(release, fromUtc, toUtc))
                {
                    release.Network = saga.Name;
                    releases.Add(release);
                }
            }
        }
        catch (ProviderUnavailableException ex)
        {
            // The film's own entry is still worth returning.
            _logger.LogDebug(
                "TMDb collection {Collection} could not be read: {Reason}",
                saga.Id,
                ex.Message);
        }

        return releases;
    }

    /// <inheritdoc />
    public async Task<ProviderTestResult> TestConnectionAsync(
        string? credentialOverride,
        CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(credentialOverride)
            ? Plugin.Config.TmdbApiKey
            : credentialOverride;

        if (string.IsNullOrWhiteSpace(key))
        {
            return ProviderTestResult.Fail("No TMDb API read access token is configured.");
        }

        try
        {
            var url = BuildUrl("configuration");
            var body = await _http
                .GetStringAsync(
                    url,
                    request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key),
                    cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(body))
            {
                return ProviderTestResult.Fail("TMDb responded but returned no data.");
            }

            return Plugin.Config.TmdbEnabled
                ? ProviderTestResult.Ok("TMDb reachable.")
                : ProviderTestResult.Ok("TMDb reachable. Tick Enabled and save to start using it.");
        }
        catch (ProviderUnavailableException ex)
        {
            return ProviderTestResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Maps a TMDb film onto the normalized series model.
    /// </summary>
    /// <param name="movie">The film.</param>
    /// <param name="imageRoot">The image CDN root to build poster URLs from.</param>
    /// <returns>The normalized record.</returns>
    internal static ProviderSeries Map(TmdbMovie movie, string imageRoot)
    {
        ArgumentNullException.ThrowIfNull(movie);

        var externalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SeriesIdentity.Tmdb] = movie.Id.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(movie.ImdbId))
        {
            externalIds[SeriesIdentity.Imdb] = movie.ImdbId;
        }

        return new ProviderSeries
        {
            Provider = ReleaseProviderKind.Tmdb,
            ProviderId = movie.Id.ToString(CultureInfo.InvariantCulture),
            Title = movie.Title ?? movie.OriginalTitle ?? string.Empty,
            OriginalTitle = movie.OriginalTitle,
            Year = ParseYear(movie.ReleaseDate),
            Status = movie.Status,
            Genres = movie.Genres?.Select(genre => genre.Name ?? string.Empty)
                .Where(name => name.Length > 0).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>(),
            Summary = TextUtil.StripHtml(movie.Overview),
            PosterUrl = BuildImageUrl(movie.PosterPath, imageRoot),
            BackdropUrl = BuildImageUrl(movie.BackdropPath, imageRoot),
            IsAnime = false,
            ExternalIds = externalIds
        };
    }

    /// <summary>
    /// Maps a TMDb film onto a calendar entry.
    /// </summary>
    /// <param name="movie">The film.</param>
    /// <param name="nowUtc">The current time, stamped as the record's freshness.</param>
    /// <param name="imageRoot">The image CDN root to build poster URLs from.</param>
    /// <returns>A release, or <see langword="null"/> when the film is already out.</returns>
    /// <remarks>
    /// A film's release is a date and never a broadcast time, so <c>HasReleaseTime</c> is always
    /// false; rendering midnight as a showtime would be inventing information. A film with no date at
    /// all is still returned when it is announced, marked
    /// <see cref="DateCertainty.AnnouncedNoDate"/>, because "the next one exists but is undated" is
    /// precisely what someone following a saga wants to know.
    /// </remarks>
    internal static ReleaseItem? MapRelease(TmdbMovie movie, DateTime nowUtc, string imageRoot)
    {
        ArgumentNullException.ThrowIfNull(movie);

        var released = ParseDate(movie.ReleaseDate);

        // Already out: there is nothing upcoming to show.
        if (released is { } date && date.Date < nowUtc.Date)
        {
            return null;
        }

        var item = new ReleaseItem
        {
            Provider = ReleaseProviderKind.Tmdb,
            ProviderId = movie.Id.ToString(CultureInfo.InvariantCulture),
            Title = movie.Title ?? movie.OriginalTitle ?? string.Empty,
            OriginalTitle = movie.OriginalTitle,
            IsAnime = false,
            Kind = ReleaseKind.Movie,
            SeasonNumber = null,
            EpisodeNumber = null,
            HasReleaseTime = false,
            SubDub = SubDubKind.Unknown,
            PosterUrl = BuildImageUrl(movie.PosterPath, imageRoot),
            BackdropUrl = BuildImageUrl(movie.BackdropPath, imageRoot),
            Summary = TextUtil.StripHtml(movie.Overview),
            Status = movie.Status,
            LastUpdatedUtc = nowUtc
        };

        if (released is { } confirmed)
        {
            item.ReleaseUtc = confirmed;

            // TMDb dates only firm up once a film is finished; before that they slip routinely.
            item.Certainty = IsDateFirm(movie.Status)
                ? DateCertainty.Exact
                : DateCertainty.Approximate;
        }
        else
        {
            item.Certainty = DateCertainty.AnnouncedNoDate;
        }

        return item;
    }

    /// <summary>
    /// Decides whether a film's release date can be presented as settled.
    /// </summary>
    /// <param name="status">TMDb's production status.</param>
    /// <returns><see langword="true"/> when the date is trustworthy.</returns>
    internal static bool IsDateFirm(string? status)
        => string.Equals(status, "Released", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Post Production", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds an absolute image URL from a TMDb path.
    /// </summary>
    /// <param name="path">The path TMDb returned, such as <c>/abc.jpg</c>.</param>
    /// <param name="imageRoot">The CDN root, including the size segment.</param>
    /// <returns>An absolute URL, or <see langword="null"/>.</returns>
    internal static string? BuildImageUrl(string? path, string imageRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var root = (string.IsNullOrWhiteSpace(imageRoot)
            ? Configuration.PluginConfiguration.DefaultTmdbImageBaseUrl
            : imageRoot).TrimEnd('/');

        return path.StartsWith('/') ? root + path : root + "/" + path;
    }

    /// <summary>
    /// Reads the configured image CDN root.
    /// </summary>
    /// <returns>The root, including its size segment.</returns>
    /// <remarks>
    /// Kept separate from the mappers so that those stay pure functions of their inputs: they can then
    /// be exercised by the test suite, which deliberately does not ship Jellyfin's assemblies.
    /// </remarks>
    private static string ImageRoot() => Plugin.Config.TmdbImageBaseUrl;

    /// <summary>
    /// Decides whether a release belongs in the requested window.
    /// </summary>
    /// <param name="item">The release.</param>
    /// <param name="fromUtc">Inclusive window start.</param>
    /// <param name="toUtc">Inclusive window end.</param>
    /// <returns><see langword="true"/> when the release should be kept.</returns>
    /// <remarks>
    /// An announced film with no date has nowhere to sit on a calendar, but it is still worth
    /// surfacing, so it is kept regardless of the window and rendered as undated.
    /// </remarks>
    internal static bool InWindow(ReleaseItem item, DateTime fromUtc, DateTime toUtc)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.ReleaseUtc is not { } date)
        {
            return item.Certainty == DateCertainty.AnnouncedNoDate;
        }

        return date >= fromUtc && date <= toUtc;
    }

    private async Task<TmdbMovie?> GetMovieAsync(string providerId, CancellationToken cancellationToken)
    {
        var url = BuildUrl("movie/" + Uri.EscapeDataString(providerId));
        return await _http.GetJsonAsync<TmdbMovie>(url, Authorize, cancellationToken).ConfigureAwait(false);
    }

    private static void Authorize(HttpRequestMessage request)
    {
        var key = Plugin.Config.TmdbApiKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
    }

    private static int? ParseYear(string? releaseDate) => ParseDate(releaseDate)?.Year;

    private static DateTime? ParseDate(string? releaseDate)
    {
        if (string.IsNullOrWhiteSpace(releaseDate))
        {
            return null;
        }

        return DateTime.TryParseExact(
            releaseDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static Uri BuildUrl(string path, params (string Key, string Value)[] query)
    {
        var configured = Plugin.Config.TmdbBaseUrl;
        var baseUrl = string.IsNullOrWhiteSpace(configured)
            ? Configuration.PluginConfiguration.DefaultTmdbBaseUrl
            : configured.TrimEnd('/');

        var builder = new UriBuilder(baseUrl + "/" + path);

        // TMDb localizes titles, synopses and genre names from this parameter, so the configured
        // language reaches the data itself rather than only the interface chrome.
        var pairs = query.ToList();
        pairs.Add(("language", ResolveLanguage()));

        builder.Query = string.Join(
            '&',
            pairs.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));

        return builder.Uri;
    }

    /// <summary>
    /// Picks the language TMDb should localize into.
    /// </summary>
    /// <returns>A TMDb language tag.</returns>
    /// <remarks>
    /// Unlike TVMaze and AnimeSchedule, TMDb does serve localized titles and synopses, so a French
    /// server gets French film summaries rather than the English fallback the other two force.
    /// </remarks>
    internal static string ResolveLanguage()
    {
        var configured = Plugin.Config.Language;
        return string.IsNullOrWhiteSpace(configured) ? "en-US" : configured;
    }
}
