using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ReleaseHub.Models;
using Jellyfin.Plugin.ReleaseHub.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ReleaseHub.Providers.AnimeSchedule;

/// <summary>
/// Release provider backed by the AnimeSchedule v3 API.
/// </summary>
/// <remarks>
/// <para>
/// Requires a bearer token created from an application in the user's AnimeSchedule account. That token
/// stays on the server: AnimeSchedule's terms explicitly forbid exposing it, and every request is made
/// by the backend on the user's behalf.
/// </para>
/// <para>
/// When the provider is switched off, or no token is configured, <see cref="IsAvailable"/> reports
/// <see langword="false"/> and every method returns empty rather than throwing, which is what lets the
/// anime half of ReleaseHub disable itself cleanly.
/// </para>
/// </remarks>
public sealed partial class AnimeScheduleProvider : IReleaseProvider, IDisposable
{
    /// <summary>
    /// AnimeSchedule documents a global limit of 120 requests per minute, applied per IP and per app.
    /// ReleaseHub stays below it rather than riding it, and <see cref="ProviderHttpClient"/> additionally
    /// honours the <c>X-RateLimit-*</c> headers returned on every response.
    /// </summary>
    private const int PermitsPerWindow = 90;

    private const string ImageRoot = "https://img.animeschedule.net/production/assets/public/img/";

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a fetched week stays usable in memory. This is not the persistent cache — it exists so
    /// that resolving many series in one synchronization does not re-request the same week each time.
    /// </summary>
    private static readonly TimeSpan WeekMemoTtl = TimeSpan.FromMinutes(5);

    private readonly ProviderHttpClient _http;
    private readonly ILogger<AnimeScheduleProvider> _logger;
    private readonly ConcurrentDictionary<string, (DateTime FetchedUtc, IReadOnlyList<ReleaseItem> Items)> _weekMemo
        = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimeScheduleProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin's HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public AnimeScheduleProvider(IHttpClientFactory httpClientFactory, ILogger<AnimeScheduleProvider> logger)
    {
        _logger = logger;
        _http = new ProviderHttpClient(httpClientFactory, logger, "AnimeSchedule", PermitsPerWindow, Window);
    }

    /// <inheritdoc />
    public ReleaseProviderKind Kind => ReleaseProviderKind.AnimeSchedule;

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            var config = Plugin.Config;
            return config.AnimeScheduleEnabled && !string.IsNullOrWhiteSpace(config.AnimeScheduleApiKey);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// One <c>/timetables</c> call returns an entire week across every airing anime, so filling a
    /// calendar window costs a handful of requests no matter how many series the user follows.
    /// </remarks>
    public bool SupportsBulkSchedule => true;

    /// <inheritdoc />
    /// <remarks>
    /// Twenty-six weeks, which is twenty-six requests per synchronization. AnimeSchedule is the one
    /// provider whose cost scales with the window, and its timetable simply does not reach a year out:
    /// seasons are announced roughly a quarter ahead, so the weeks past this point would cost a request
    /// each to return nothing. The rest of the horizon is still covered — by the providers it costs
    /// nothing to ask.
    /// </remarks>
    public TimeSpan? MaxLookAhead => TimeSpan.FromDays(182);

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

        // The API caps q at 200 characters; trimming here avoids a guaranteed 400.
        var trimmed = query.Length > 200 ? query[..200] : query;

        var url = BuildUrl("anime", ("q", trimmed));
        var page = await _http
            .GetJsonAsync<AnimeSchedulePage>(url, Authorize, cancellationToken)
            .ConfigureAwait(false);

        if (page?.Anime is null)
        {
            return Array.Empty<ProviderSeries>();
        }

        return page.Anime.Take(limit).Select(Map).ToList();
    }

    /// <inheritdoc />
    public async Task<ProviderSeries?> GetSeriesAsync(string providerId, CancellationToken cancellationToken)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        var url = BuildUrl("anime/" + Uri.EscapeDataString(providerId));
        var anime = await _http
            .GetJsonAsync<AnimeScheduleAnime>(url, Authorize, cancellationToken)
            .ConfigureAwait(false);

        return anime is null ? null : Map(anime);
    }

    /// <summary>
    /// Jellyfin provider id keys paired with the <c>/anime</c> filter that looks them up.
    /// </summary>
    /// <remarks>
    /// Ordered by how reliably each database identifies a single broadcast run. AniDB is the most
    /// granular, which matters because ReleaseHub schedules per broadcast rather than per franchise.
    /// </remarks>
    private static readonly (string Key, string Filter)[] IdentifierFilters =
    [
        (SeriesIdentity.AniDb, "anidb-ids"),
        (SeriesIdentity.AniList, "anilist-ids"),
        (SeriesIdentity.MyAnimeList, "mal-ids")
    ];

    /// <inheritdoc />
    public async Task<ProviderMatch?> ResolveAsync(SeriesIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (!IsAvailable)
        {
            return null;
        }

        // Identifier filters first. Anime titles differ wildly between databases — romaji, English,
        // native, abbreviations, season suffixes — so an id lookup is the only reliable way to be
        // certain, and it costs the same single request that a title search would.
        foreach (var (key, filter) in IdentifierFilters)
        {
            var value = identity.GetProviderId(key);
            if (value is null || !IsNumeric(value))
            {
                continue;
            }

            var url = BuildUrl("anime", (filter, value));
            var page = await _http
                .GetJsonAsync<AnimeSchedulePage>(url, Authorize, cancellationToken)
                .ConfigureAwait(false);

            // Verify rather than assume. The id filter is not guaranteed to return exactly one anime —
            // a franchise's movies and spin-offs come back alongside the series, ordered by
            // popularity — so taking the first result blindly is how a library's "One Piece" ends up
            // mapped to "one-piece-movie" and its calendar silently empties. Only a candidate that
            // actually carries the identifier we searched for is an exact match.
            if (page?.Anime is { Count: > 0 } hits)
            {
                foreach (var candidate in hits)
                {
                    var ids = ExtractExternalIds(candidate.Websites);
                    if (!ids.TryGetValue(key, out var found)
                        || !string.Equals(found, value, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // An identifier can be right and still point at the wrong thing. Jellyfin's
                    // metadata for a series sometimes carries a film's id from the same franchise —
                    // One Piece is stored with AniDB 411, which is the 2000 movie, while the TV run
                    // is AniDB 69. Accepting it produced a confident mapping to a film that has no
                    // broadcast schedule, silently emptying that series' calendar. A film can never
                    // be the match for a series, whatever the id says.
                    if (!IsPlausibleSeries(candidate))
                    {
                        _logger.LogDebug(
                            "AnimeSchedule {Route} matches {Key} {Value} but is a film, not a series; "
                            + "ignoring it and trying the next identifier",
                            candidate.Route,
                            key,
                            value);
                        continue;
                    }

                    return new ProviderMatch(Map(candidate), MatchConfidence.Exact, $"{key} id match");
                }

                _logger.LogDebug(
                    "AnimeSchedule returned {Count} result(s) for {Key} {Value} but none carried that id back; "
                    + "falling through to title matching",
                    hits.Count,
                    key,
                    value);
            }
        }

        // Only then fall back to titles, and never accept a weak result blindly.
        var candidates = await SearchAsync(identity.Title, 15, cancellationToken).ConfigureAwait(false);

        var match = SeriesMatcher.PickBest(identity, candidates);
        if (match is not null || string.IsNullOrWhiteSpace(identity.OriginalTitle))
        {
            return match;
        }

        // Jellyfin's title and AnimeSchedule's may be in different scripts entirely; the original title
        // is often the one that lands.
        var byOriginal = await SearchAsync(identity.OriginalTitle, 15, cancellationToken).ConfigureAwait(false);
        return SeriesMatcher.PickBest(identity, byOriginal);
    }

    /// <summary>
    /// Decides whether an AnimeSchedule entry could be the ongoing run of a series.
    /// </summary>
    /// <param name="anime">The candidate.</param>
    /// <returns><see langword="false"/> when the entry is only ever a film.</returns>
    /// <remarks>
    /// Deliberately narrow: only entries whose media types are exclusively films are rejected. ONA,
    /// OVA, Special and TV Short are all legitimate ways an episodic release is catalogued, and
    /// excluding them would lose real series. An entry with no media types at all is kept, because
    /// absent data is not evidence.
    /// </remarks>
    internal static bool IsPlausibleSeries(AnimeScheduleAnime anime)
    {
        ArgumentNullException.ThrowIfNull(anime);

        var types = anime.MediaTypes;
        if (types is null || types.Count == 0)
        {
            return true;
        }

        foreach (var type in types)
        {
            if (!string.Equals(type.Name, "Movie", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks that an identifier is a bare number, as the id filters require.
    /// </summary>
    /// <param name="value">The candidate identifier.</param>
    /// <returns><see langword="true"/> when it is safe to send as an id filter.</returns>
    /// <remarks>
    /// Jellyfin stores whatever a metadata plugin wrote, which is not always a plain integer. Sending a
    /// malformed id would spend a request on a guaranteed empty result.
    /// </remarks>
    private static bool IsNumeric(string value)
    {
        foreach (var ch in value)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReleaseItem>> GetScheduleAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return Array.Empty<ReleaseItem>();
        }

        var releases = new List<ReleaseItem>();

        foreach (var (year, week) in EnumerateIsoWeeks(fromUtc, toUtc))
        {
            IReadOnlyList<ReleaseItem> weekItems;
            try
            {
                weekItems = await GetWeekAsync(year, week, cancellationToken).ConfigureAwait(false);
            }
            catch (ProviderUnavailableException ex)
            {
                // Partial data beats no data: keep the weeks already fetched and let the caller fall
                // back to cache for the rest.
                _logger.LogWarning(
                    "AnimeSchedule week {Week}/{Year} could not be fetched: {Reason}",
                    week,
                    year,
                    ex.Message);
                continue;
            }

            foreach (var item in weekItems)
            {
                if (item.ReleaseUtc is { } releaseUtc && releaseUtc >= fromUtc && releaseUtc <= toUtc)
                {
                    releases.Add(item);
                }
            }
        }

        return releases;
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

        var schedule = await GetScheduleAsync(fromUtc, toUtc, cancellationToken).ConfigureAwait(false);

        return schedule
            .Where(item => string.Equals(item.ProviderId, series.ProviderId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ProviderTestResult> TestConnectionAsync(
        string? credentialOverride,
        CancellationToken cancellationToken)
    {
        // Testing a key typed into the settings page but not yet saved is the whole point: it lets an
        // administrator confirm a key before replacing a working one. The override is never persisted.
        var key = string.IsNullOrWhiteSpace(credentialOverride)
            ? Plugin.Config.AnimeScheduleApiKey
            : credentialOverride;

        if (string.IsNullOrWhiteSpace(key))
        {
            return ProviderTestResult.Fail("No AnimeSchedule API key is configured.");
        }

        try
        {
            var now = DateTime.UtcNow;
            var url = BuildUrl(
                "timetables/all",
                ("year", ISOWeek.GetYear(now).ToString(CultureInfo.InvariantCulture)),
                ("week", ISOWeek.GetWeekOfYear(now).ToString(CultureInfo.InvariantCulture)),
                ("tz", "UTC"));

            var entries = await _http
                .GetJsonAsync<List<AnimeScheduleTimetableEntry>>(
                    url,
                    request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key),
                    cancellationToken)
                .ConfigureAwait(false);

            if (entries is null)
            {
                return ProviderTestResult.Fail("AnimeSchedule responded but returned no data.");
            }

            var message = string.Create(
                CultureInfo.InvariantCulture,
                $"AnimeSchedule reachable; {entries.Count} entries scheduled this week.");

            // Reaching the API is only half the story: with the provider switched off nothing will be
            // fetched, and an administrator who just validated a key deserves to be told that.
            return Plugin.Config.AnimeScheduleEnabled
                ? ProviderTestResult.Ok(message)
                : ProviderTestResult.Ok(message + " Tick \"Enabled\" and save to start using it.");
        }
        catch (ProviderUnavailableException ex)
        {
            // ex.Message never contains the key: ProviderHttpClient reports only the status code.
            return ProviderTestResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Maps an AnimeSchedule anime onto the normalized model.
    /// </summary>
    /// <param name="anime">The AnimeSchedule anime.</param>
    /// <returns>The normalized series.</returns>
    internal static ProviderSeries Map(AnimeScheduleAnime anime)
    {
        ArgumentNullException.ThrowIfNull(anime);

        var alternates = new List<string>();
        void AddAlternate(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !alternates.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                alternates.Add(value);
            }
        }

        AddAlternate(anime.Names?.Romaji);
        AddAlternate(anime.Names?.English);
        AddAlternate(anime.Names?.Native);
        AddAlternate(anime.Names?.Abbreviation);

        if (anime.Names?.Synonyms is { } synonyms)
        {
            foreach (var synonym in synonyms)
            {
                AddAlternate(synonym);
            }
        }

        return new ProviderSeries
        {
            Provider = ReleaseProviderKind.AnimeSchedule,
            ProviderId = anime.Route ?? string.Empty,
            Title = anime.Title ?? anime.Names?.English ?? anime.Names?.Romaji ?? string.Empty,
            OriginalTitle = anime.Names?.Native ?? anime.Names?.Romaji,
            AlternateTitles = alternates,
            Year = anime.Year ?? anime.Season?.Year,
            Status = anime.Status,
            Genres = anime.Genres?.Select(genre => genre.Name ?? string.Empty)
                .Where(name => name.Length > 0).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>(),
            Summary = TextUtil.StripHtml(anime.Description),
            PosterUrl = BuildImageUrl(anime.ImageVersionRoute),
            StreamingPlatform = FirstStreamingService(anime.Websites?.Streams),
            IsAnime = true,
            ExternalIds = ExtractExternalIds(anime.Websites)
        };
    }

    /// <summary>
    /// Maps a timetable entry onto the normalized model.
    /// </summary>
    /// <param name="entry">The timetable entry.</param>
    /// <param name="nowUtc">The current time, stamped as the record's freshness.</param>
    /// <returns>A release, or <see langword="null"/> when the entry has no usable date.</returns>
    internal static ReleaseItem? MapTimetableEntry(AnimeScheduleTimetableEntry entry, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // AnimeSchedule uses 0001-01-01T00:00:00Z as its null date; treating it as a real broadcast
        // would put every unscheduled anime at the start of the calendar.
        if (entry.EpisodeDate is not { } episodeDate || episodeDate.Year <= 1)
        {
            return null;
        }

        var title = entry.Title ?? entry.English ?? entry.Romaji ?? string.Empty;

        // A delayed slot is a real schedule change, not a firm broadcast. Downgrading its certainty is
        // what stops the calendar from presenting a moved episode as confirmed.
        var isDelayed = string.Equals(entry.AiringStatus, "delayed-air", StringComparison.OrdinalIgnoreCase);

        return new ReleaseItem
        {
            Provider = ReleaseProviderKind.AnimeSchedule,
            ProviderId = entry.Route ?? string.Empty,
            Title = title,
            OriginalTitle = entry.Native ?? entry.Romaji,
            IsAnime = true,
            Kind = entry.EpisodeNumber == 1 ? ReleaseKind.SeasonPremiere : ReleaseKind.Episode,
            EpisodeNumber = entry.EpisodeNumber,

            // AnimeSchedule schedules per broadcast slot rather than per season, so a season number is
            // genuinely unknown here. Leaving it null makes the UI render "Episode 1150" rather than
            // inventing a season that does not exist.
            SeasonNumber = null,
            ReleaseUtc = episodeDate.UtcDateTime,
            HasReleaseTime = true,
            ReleaseTimezone = episodeDate.Offset.ToString(),
            Certainty = isDelayed ? DateCertainty.Approximate : DateCertainty.Exact,
            ApproximateLabel = isDelayed ? entry.DelayedText : null,
            SubDub = ParseAirType(entry.AirType),
            StreamingPlatform = FirstStreamingService(entry.Streams),
            PosterUrl = BuildImageUrl(entry.ImageVersionRoute),
            Status = entry.Status,
            LastUpdatedUtc = nowUtc
        };
    }

    /// <summary>
    /// Formats the episode range for a slot that airs several episodes at once.
    /// </summary>
    /// <param name="entry">The timetable entry.</param>
    /// <returns>A label such as <c>7-12</c>, or <see langword="null"/> for a single episode.</returns>
    /// <remarks>
    /// AnimeSchedule expresses a batch as <c>subtractedEpisodeNumber - episodeNumber</c>. Showing only
    /// the last number would hide that several episodes drop at once.
    /// </remarks>
    internal static string? FormatEpisodeRange(AnimeScheduleTimetableEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.EpisodeNumber is not { } last
            || entry.SubtractedEpisodeNumber is not { } first
            || first <= 0
            || first >= last)
        {
            return null;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{first}-{last}");
    }

    /// <summary>
    /// Parses an AnimeSchedule air type string.
    /// </summary>
    /// <param name="airType">The raw value.</param>
    /// <returns>The parsed variant.</returns>
    internal static SubDubKind ParseAirType(string? airType) => airType?.ToLowerInvariant() switch
    {
        "sub" => SubDubKind.Sub,
        "dub" => SubDubKind.Dub,
        "raw" => SubDubKind.Raw,
        _ => SubDubKind.Unknown
    };

    /// <summary>
    /// Extracts third-party identifiers from AnimeSchedule's website links.
    /// </summary>
    /// <param name="websites">The website links.</param>
    /// <returns>Identifiers keyed by <see cref="SeriesIdentity"/>'s well-known names.</returns>
    /// <remarks>
    /// AnimeSchedule publishes URLs, not bare identifiers, so the numeric id is pulled out of the path.
    /// These are the only way to match anime against Jellyfin exactly, since anime titles differ wildly
    /// between databases.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> ExtractExternalIds(AnimeScheduleWebsites? websites)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (websites is null)
        {
            return ids;
        }

        AddIfFound(ids, SeriesIdentity.AniList, websites.AniList);
        AddIfFound(ids, SeriesIdentity.AniDb, websites.AniDb);
        AddIfFound(ids, SeriesIdentity.MyAnimeList, websites.MyAnimeList);

        return ids;
    }

    private static void AddIfFound(Dictionary<string, string> ids, string key, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var match = TrailingIdRegex().Match(url);
        if (match.Success)
        {
            ids[key] = match.Groups[1].Value;
        }
    }

    private async Task<IReadOnlyList<ReleaseItem>> GetWeekAsync(
        int year,
        int week,
        CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"{year}-{week}");

        if (_weekMemo.TryGetValue(key, out var memo)
            && DateTime.UtcNow - memo.FetchedUtc < WeekMemoTtl)
        {
            return memo.Items;
        }

        var url = BuildUrl(
            "timetables/all",
            ("year", year.ToString(CultureInfo.InvariantCulture)),
            ("week", week.ToString(CultureInfo.InvariantCulture)),

            // Ask for UTC so no local-time conversion happens before ReleaseHub sees the data; the
            // frontend renders in each viewer's own timezone.
            ("tz", "UTC"));

        var entries = await _http
            .GetJsonAsync<List<AnimeScheduleTimetableEntry>>(url, Authorize, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTime.UtcNow;
        var items = entries is null
            ? (IReadOnlyList<ReleaseItem>)Array.Empty<ReleaseItem>()
            : entries.Select(entry => MapTimetableEntry(entry, now))
                .Where(item => item is not null)
                .Select(item => item!)
                .ToList();

        _weekMemo[key] = (now, items);
        return items;
    }

    private static IEnumerable<(int Year, int Week)> EnumerateIsoWeeks(DateTime fromUtc, DateTime toUtc)
    {
        var seen = new HashSet<(int, int)>();

        for (var day = fromUtc.Date; day <= toUtc.Date; day = day.AddDays(1))
        {
            var pair = (ISOWeek.GetYear(day), ISOWeek.GetWeekOfYear(day));
            if (seen.Add(pair))
            {
                yield return pair;
            }
        }
    }

    private static void Authorize(HttpRequestMessage request)
    {
        var key = Plugin.Config.AnimeScheduleApiKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
    }

    private static string? BuildImageUrl(string? imageVersionRoute)
        => string.IsNullOrWhiteSpace(imageVersionRoute) ? null : ImageRoot + imageVersionRoute;

    /// <summary>
    /// Picks a streaming platform to display from AnimeSchedule's stream list.
    /// </summary>
    /// <param name="streams">The stream entries, which may be <see langword="null"/>.</param>
    /// <returns>A display label, or <see langword="null"/> when none was supplied.</returns>
    /// <remarks>
    /// Reads whatever platforms the API returns instead of testing a fixed set of names: API 1.1 made
    /// streams a flexible array precisely so that new platforms would not require a client change.
    /// </remarks>
    internal static string? FirstStreamingService(IReadOnlyList<AnimeScheduleStreamEntry>? streams)
    {
        if (streams is null || streams.Count == 0)
        {
            return null;
        }

        foreach (var stream in streams)
        {
            // "name" is the human label, e.g. "Crunchyroll (Raw)"; "platform" is the bare slug.
            if (!string.IsNullOrWhiteSpace(stream.Name))
            {
                return stream.Name;
            }

            if (!string.IsNullOrWhiteSpace(stream.Platform))
            {
                return stream.Platform;
            }
        }

        return null;
    }

    private static Uri BuildUrl(string path, params (string Key, string Value)[] query)
    {
        var configured = Plugin.Config.AnimeScheduleBaseUrl;
        var baseUrl = string.IsNullOrWhiteSpace(configured)
            ? Configuration.PluginConfiguration.DefaultAnimeScheduleBaseUrl
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

    /// <summary>
    /// Matches the trailing numeric identifier of a database URL, such as
    /// <c>https://anilist.co/anime/12345</c> or <c>https://anidb.net/anime/1234</c>.
    /// </summary>
    [GeneratedRegex(@"/(\d+)/?(?:[?#].*)?$", RegexOptions.Compiled)]
    private static partial Regex TrailingIdRegex();
}
