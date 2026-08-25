using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ReleaseHub.Models;
using Jellyfin.Plugin.ReleaseHub.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ReleaseHub.Providers.TvMaze;

/// <summary>
/// Release provider backed by the public TVMaze API.
/// </summary>
/// <remarks>
/// TVMaze needs no API key for public use. Its data is licensed CC BY-SA, so anything built on it must
/// credit TVMaze — ReleaseHub does so on its settings page and in its documentation.
/// </remarks>
public sealed class TvMazeProvider : IReleaseProvider, IDisposable
{
    /// <summary>
    /// TVMaze documents "at least 20 calls every 10 seconds". Staying meaningfully under that is a
    /// deliberate choice: the published figure is a floor, not a guarantee, and the plugin has no
    /// reason to explore where the real ceiling is.
    /// </summary>
    private const int PermitsPerWindow = 12;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly ProviderHttpClient _http;
    private readonly ILogger<TvMazeProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvMazeProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin's HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public TvMazeProvider(IHttpClientFactory httpClientFactory, ILogger<TvMazeProvider> logger)
    {
        _logger = logger;
        _http = new ProviderHttpClient(httpClientFactory, logger, "TVMaze", PermitsPerWindow, Window);
    }

    /// <inheritdoc />
    public ReleaseProviderKind Kind => ReleaseProviderKind.TvMaze;

    /// <inheritdoc />
    public bool IsAvailable => Plugin.Config.TvMazeEnabled;

    /// <inheritdoc />
    /// <remarks>
    /// TVMaze's <c>/schedule/full</c> is documented as "at least several MB" covering every show it
    /// knows. Pulling that to find a handful of library series would be wasteful for both sides, so
    /// ReleaseHub uses the per-series path instead.
    /// </remarks>
    public bool SupportsBulkSchedule => false;

    /// <inheritdoc />
    /// <remarks>
    /// A show's entire episode list arrives in the same single request however narrow the window is
    /// asked for, so narrowing it would save nothing and only hide episodes already downloaded.
    /// </remarks>
    public TimeSpan? MaxLookAhead => null;

    /// <inheritdoc />
    public Task<IReadOnlyList<ReleaseItem>> GetScheduleAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ReleaseItem>>(Array.Empty<ReleaseItem>());

    /// <summary>
    /// Gets the number of requests this provider has issued since the server started.
    /// </summary>
    public int RequestCount => _http.RequestCount;

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

        var url = BuildUrl("search/shows", ("q", query));
        var results = await _http
            .GetJsonAsync<List<TvMazeSearchResult>>(url, null, cancellationToken)
            .ConfigureAwait(false);

        if (results is null)
        {
            return Array.Empty<ProviderSeries>();
        }

        return results
            .Where(result => result.Show is not null)
            .Take(limit)
            .Select(result => Map(result.Show!))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ProviderSeries?> GetSeriesAsync(string providerId, CancellationToken cancellationToken)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        var url = BuildUrl("shows/" + Uri.EscapeDataString(providerId));
        var show = await _http.GetJsonAsync<TvMazeShow>(url, null, cancellationToken).ConfigureAwait(false);

        return show is null ? null : Map(show);
    }

    /// <inheritdoc />
    public async Task<ProviderMatch?> ResolveAsync(SeriesIdentity identity, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return null;
        }

        // 1. TVMaze's own id, if some metadata plugin already stored one. Nothing beats this.
        var tvMazeId = identity.GetProviderId(SeriesIdentity.TvMaze);
        if (tvMazeId is not null)
        {
            var direct = await GetSeriesAsync(tvMazeId, cancellationToken).ConfigureAwait(false);
            if (direct is not null)
            {
                return new ProviderMatch(direct, MatchConfidence.Exact, "TVMaze id from Jellyfin");
            }
        }

        // 2. Cross-database identifier lookup. TVMaze redirects these to the canonical show, so an
        //    answer here is authoritative regardless of how the title is spelled in either database.
        foreach (var (key, parameter) in IdentifierLookups)
        {
            var value = identity.GetProviderId(key);
            if (value is null)
            {
                continue;
            }

            var url = BuildUrl("lookup/shows", (parameter, value));
            var show = await _http.GetJsonAsync<TvMazeShow>(url, null, cancellationToken).ConfigureAwait(false);
            if (show is not null)
            {
                return new ProviderMatch(Map(show), MatchConfidence.Exact, $"{key} id match");
            }
        }

        // 3. Title matching, only as a last resort and never accepted blindly.
        var candidates = await SearchAsync(identity.Title, 10, cancellationToken).ConfigureAwait(false);
        return SeriesMatcher.PickBest(identity, candidates);
    }

    /// <inheritdoc />
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

        // One request returns the show and its whole episode list, which is cheaper than paging the
        // schedule and then filtering it down to the series the user actually cares about.
        var url = BuildUrl("shows/" + Uri.EscapeDataString(series.ProviderId), ("embed", "episodes"));
        var show = await _http.GetJsonAsync<TvMazeShow>(url, null, cancellationToken).ConfigureAwait(false);

        var episodes = show?.Embedded?.Episodes;
        if (episodes is null || episodes.Count == 0)
        {
            return Array.Empty<ReleaseItem>();
        }

        var mapped = Map(show!);
        var now = DateTime.UtcNow;
        var releases = new List<ReleaseItem>();

        foreach (var episode in episodes)
        {
            var release = MapEpisode(mapped, episode, now);
            if (release?.ReleaseUtc is not { } releaseUtc)
            {
                continue;
            }

            if (releaseUtc >= fromUtc && releaseUtc <= toUtc)
            {
                releases.Add(release);
            }
        }

        return releases;
    }

    /// <inheritdoc />
    /// <remarks>TVMaze needs no credential, so <paramref name="credentialOverride"/> is ignored.</remarks>
    public async Task<ProviderTestResult> TestConnectionAsync(
        string? credentialOverride,
        CancellationToken cancellationToken)
    {
        if (!Plugin.Config.TvMazeEnabled)
        {
            return ProviderTestResult.Fail("TVMaze is disabled.");
        }

        try
        {
            var url = BuildUrl("shows/1");
            var show = await _http.GetJsonAsync<TvMazeShow>(url, null, cancellationToken).ConfigureAwait(false);

            return show is null
                ? ProviderTestResult.Fail("TVMaze responded but returned no data.")
                : ProviderTestResult.Ok("TVMaze reachable.");
        }
        catch (ProviderUnavailableException ex)
        {
            return ProviderTestResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Maps a TVMaze episode onto the normalized model.
    /// </summary>
    /// <param name="series">The already-mapped series this episode belongs to.</param>
    /// <param name="episode">The TVMaze episode.</param>
    /// <param name="nowUtc">The current time, stamped as the record's freshness.</param>
    /// <returns>A release, or <see langword="null"/> when TVMaze gave no usable date.</returns>
    /// <remarks>
    /// Internal rather than private so the mapping — the part most likely to break when a provider
    /// changes its output — can be unit tested against captured fixtures without any network access.
    /// </remarks>
    internal static ReleaseItem? MapEpisode(ProviderSeries series, TvMazeEpisode episode, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episode);

        if (episode.AirStamp is not { } airStamp)
        {
            return null;
        }

        // TVMaze still emits an airstamp when it has no airtime, synthesising midday UTC. Trusting it
        // would put a fabricated broadcast time on the calendar, so the empty airtime is what decides.
        var hasTime = !string.IsNullOrWhiteSpace(episode.AirTime);

        return new ReleaseItem
        {
            Provider = ReleaseProviderKind.TvMaze,
            ProviderId = series.ProviderId,
            Title = series.Title,
            OriginalTitle = series.OriginalTitle,
            IsAnime = series.IsAnime,
            Kind = ClassifyEpisode(episode),
            SeasonNumber = episode.Season,
            EpisodeNumber = episode.Number,
            EpisodeTitle = episode.Name,
            ReleaseUtc = airStamp.UtcDateTime,
            HasReleaseTime = hasTime,
            ReleaseTimezone = hasTime ? airStamp.Offset.ToString() : null,
            Certainty = DateCertainty.Exact,
            SubDub = SubDubKind.Unknown,
            Network = series.Network,
            StreamingPlatform = series.StreamingPlatform,
            PosterUrl = series.PosterUrl,
            BackdropUrl = series.BackdropUrl,
            Summary = TextUtil.StripHtml(episode.Summary) ?? series.Summary,
            Status = series.Status,
            LastUpdatedUtc = nowUtc
        };
    }

    /// <summary>
    /// Maps a TVMaze show onto the normalized model.
    /// </summary>
    /// <param name="show">The TVMaze show.</param>
    /// <returns>The normalized series.</returns>
    internal static ProviderSeries Map(TvMazeShow show)
    {
        ArgumentNullException.ThrowIfNull(show);

        var externalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SeriesIdentity.TvMaze] = show.Id.ToString(CultureInfo.InvariantCulture)
        };

        if (show.Externals?.TheTvDb is { } tvdb)
        {
            externalIds[SeriesIdentity.Tvdb] = tvdb.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(show.Externals?.Imdb))
        {
            externalIds[SeriesIdentity.Imdb] = show.Externals.Imdb;
        }

        return new ProviderSeries
        {
            Provider = ReleaseProviderKind.TvMaze,
            ProviderId = show.Id.ToString(CultureInfo.InvariantCulture),
            Title = show.Name ?? string.Empty,
            Year = ParseYear(show.Premiered),
            Status = show.Status,
            Genres = show.Genres ?? Array.Empty<string>(),
            Summary = TextUtil.StripHtml(show.Summary),
            PosterUrl = show.Image?.Original ?? show.Image?.Medium,
            Network = show.Network?.Name,
            StreamingPlatform = show.WebChannel?.Name,
            IsAnime = LooksLikeAnime(show),
            ExternalIds = externalIds
        };
    }

    /// <summary>
    /// Decides whether a TVMaze show should be treated as anime.
    /// </summary>
    /// <param name="show">The show.</param>
    /// <returns><see langword="true"/> when the show looks like anime.</returns>
    /// <remarks>
    /// TVMaze has no anime flag, so this leans on the two signals it does publish: the Anime genre, and
    /// Japanese-language animation. It is only a hint — the authoritative signal is an anime-specific
    /// provider id on the Jellyfin item, which the resolver checks first.
    /// </remarks>
    internal static bool LooksLikeAnime(TvMazeShow show)
    {
        ArgumentNullException.ThrowIfNull(show);

        var genres = show.Genres;
        if (genres is not null
            && genres.Any(genre => string.Equals(genre, "Anime", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return string.Equals(show.Type, "Animation", StringComparison.OrdinalIgnoreCase)
            && string.Equals(show.Language, "Japanese", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly (string Key, string Parameter)[] IdentifierLookups =
    [
        (SeriesIdentity.Tvdb, "thetvdb"),
        (SeriesIdentity.Imdb, "imdb")
    ];

    private static ReleaseKind ClassifyEpisode(TvMazeEpisode episode)
    {
        if (!string.IsNullOrEmpty(episode.Type)
            && episode.Type.Contains("special", StringComparison.OrdinalIgnoreCase))
        {
            return ReleaseKind.Special;
        }

        return episode.Number == 1 ? ReleaseKind.SeasonPremiere : ReleaseKind.Episode;
    }

    private static int? ParseYear(string? premiered)
    {
        if (string.IsNullOrWhiteSpace(premiered) || premiered.Length < 4)
        {
            return null;
        }

        return int.TryParse(
            premiered.AsSpan(0, 4),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var year)
            ? year
            : null;
    }

    private static Uri BuildUrl(string path, params (string Key, string Value)[] query)
    {
        var configured = Plugin.Config.TvMazeBaseUrl;
        var baseUrl = string.IsNullOrWhiteSpace(configured)
            ? Configuration.PluginConfiguration.DefaultTvMazeBaseUrl
            : configured.TrimEnd('/');

        var builder = new UriBuilder(baseUrl + "/" + path);

        if (query.Length > 0)
        {
            builder.Query = string.Join(
                '&',
                query.Select(pair =>
                    Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        }

        return builder.Uri;
    }
}
