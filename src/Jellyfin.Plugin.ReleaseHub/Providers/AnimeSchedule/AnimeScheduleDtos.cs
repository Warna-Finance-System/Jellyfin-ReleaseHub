using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ReleaseHub.Providers.AnimeSchedule;

/// <summary>
/// A page of results from <c>GET /anime</c>.
/// </summary>
/// <remarks>Each page carries up to 18 anime.</remarks>
public sealed class AnimeSchedulePage
{
    /// <summary>Gets or sets the page number.</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>Gets or sets the total number of anime matching the query.</summary>
    [JsonPropertyName("totalAmount")]
    public int TotalAmount { get; set; }

    /// <summary>Gets or sets the anime on this page.</summary>
    [JsonPropertyName("anime")]
    public IReadOnlyList<AnimeScheduleAnime>? Anime { get; set; }
}

/// <summary>
/// An anime as returned by <c>GET /anime</c> and <c>GET /anime/{slug}</c>.
/// </summary>
public sealed class AnimeScheduleAnime
{
    /// <summary>Gets or sets the unique identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the title, used as a high-priority name.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the unique URL slug, which is the identifier every other endpoint expects.
    /// </summary>
    [JsonPropertyName("route")]
    public string? Route { get; set; }

    /// <summary>Gets or sets the Japanese release date of the first episode.</summary>
    [JsonPropertyName("premier")]
    public DateTimeOffset? Premier { get; set; }

    /// <summary>Gets or sets the English sub release date of the first episode.</summary>
    [JsonPropertyName("subPremier")]
    public DateTimeOffset? SubPremier { get; set; }

    /// <summary>Gets or sets the English dub release date of the first episode.</summary>
    [JsonPropertyName("dubPremier")]
    public DateTimeOffset? DubPremier { get; set; }

    /// <summary>Gets or sets the earliest month of the release date.</summary>
    [JsonPropertyName("month")]
    public string? Month { get; set; }

    /// <summary>Gets or sets the earliest year of the release date.</summary>
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    /// <summary>Gets or sets the broadcast season.</summary>
    [JsonPropertyName("season")]
    public AnimeScheduleSeason? Season { get; set; }

    /// <summary>Gets or sets the delayed text shown on the timetable.</summary>
    [JsonPropertyName("delayedTimetable")]
    public string? DelayedTimetable { get; set; }

    /// <summary>Gets or sets the date the anime was delayed from.</summary>
    [JsonPropertyName("delayedFrom")]
    public DateTimeOffset? DelayedFrom { get; set; }

    /// <summary>Gets or sets the date the anime was delayed until.</summary>
    [JsonPropertyName("delayedUntil")]
    public DateTimeOffset? DelayedUntil { get; set; }

    /// <summary>Gets or sets the number of episodes.</summary>
    [JsonPropertyName("episodes")]
    public int? Episodes { get; set; }

    /// <summary>Gets or sets the per-episode length in minutes.</summary>
    [JsonPropertyName("lengthMin")]
    public int? LengthMin { get; set; }

    /// <summary>Gets or sets the airing status: <c>Finished</c>, <c>Ongoing</c> or <c>Delayed</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Gets or sets the HTML description.</summary>
    /// <remarks>
    /// Removed in API 1.17 in favour of <c>synopsis</c> and restored in 1.20, which is the behaviour
    /// ReleaseHub targets. The mapper tolerates it being absent.
    /// </remarks>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets or sets the poster/image URL slug.</summary>
    [JsonPropertyName("imageVersionRoute")]
    public string? ImageVersionRoute { get; set; }

    /// <summary>Gets or sets the alternative names.</summary>
    [JsonPropertyName("names")]
    public AnimeScheduleNames? Names { get; set; }

    /// <summary>Gets or sets the genres.</summary>
    [JsonPropertyName("genres")]
    public IReadOnlyList<AnimeScheduleCategory>? Genres { get; set; }

    /// <summary>Gets or sets the studios.</summary>
    [JsonPropertyName("studios")]
    public IReadOnlyList<AnimeScheduleCategory>? Studios { get; set; }

    /// <summary>Gets or sets the media types, for example <c>TV</c> or <c>ONA</c>.</summary>
    [JsonPropertyName("mediaTypes")]
    public IReadOnlyList<AnimeScheduleCategory>? MediaTypes { get; set; }

    /// <summary>Gets or sets the official website and third-party database links.</summary>
    [JsonPropertyName("websites")]
    public AnimeScheduleWebsites? Websites { get; set; }
}

/// <summary>
/// A broadcast season descriptor.
/// </summary>
public sealed class AnimeScheduleSeason
{
    /// <summary>Gets or sets the season title, for example <c>Winter 2017</c>.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the year the season is in.</summary>
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    /// <summary>Gets or sets the calendar season, for example <c>Winter</c>.</summary>
    [JsonPropertyName("season")]
    public string? Season { get; set; }

    /// <summary>Gets or sets the slug, for example <c>winter-2017</c>.</summary>
    [JsonPropertyName("route")]
    public string? Route { get; set; }
}

/// <summary>
/// Alternative titles for an anime.
/// </summary>
/// <remarks>
/// These carry most of the matching value for anime: Jellyfin libraries store romaji, English or native
/// titles depending on which metadata plugin filled them in, and ReleaseHub has to match all of them.
/// </remarks>
public sealed class AnimeScheduleNames
{
    /// <summary>Gets or sets the romanized title.</summary>
    [JsonPropertyName("romaji")]
    public string? Romaji { get; set; }

    /// <summary>Gets or sets the English title.</summary>
    [JsonPropertyName("english")]
    public string? English { get; set; }

    /// <summary>Gets or sets the native-script title.</summary>
    [JsonPropertyName("native")]
    public string? Native { get; set; }

    /// <summary>Gets or sets the common abbreviation.</summary>
    [JsonPropertyName("abbreviation")]
    public string? Abbreviation { get; set; }

    /// <summary>Gets or sets further synonyms.</summary>
    [JsonPropertyName("synonyms")]
    public IReadOnlyList<string>? Synonyms { get; set; }
}

/// <summary>
/// A named, slugged category such as a genre, studio, source or media type.
/// </summary>
public sealed class AnimeScheduleCategory
{
    /// <summary>Gets or sets the display name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the unique URL slug.</summary>
    [JsonPropertyName("route")]
    public string? Route { get; set; }
}

/// <summary>
/// One streaming platform an anime is available on.
/// </summary>
/// <remarks>
/// API 1.1 replaced the previous hardcoded per-platform fields with this flexible array, so platforms
/// must be read from the data rather than from a fixed list of property names.
/// </remarks>
public sealed class AnimeScheduleStreamEntry
{
    /// <summary>Gets or sets the platform identifier, for example <c>crunchyroll</c>.</summary>
    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    /// <summary>Gets or sets the URL to the anime on that platform.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Gets or sets the display label, for example <c>Crunchyroll (Raw)</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Official website, third-party database links and streaming platforms.
/// </summary>
public sealed class AnimeScheduleWebsites
{
    /// <summary>Gets or sets the official site URL.</summary>
    [JsonPropertyName("official")]
    public string? Official { get; set; }

    /// <summary>Gets or sets the MyAnimeList URL.</summary>
    [JsonPropertyName("mal")]
    public string? MyAnimeList { get; set; }

    /// <summary>Gets or sets the AniList URL.</summary>
    [JsonPropertyName("aniList")]
    public string? AniList { get; set; }

    /// <summary>Gets or sets the Kitsu URL.</summary>
    [JsonPropertyName("kitsu")]
    public string? Kitsu { get; set; }

    /// <summary>Gets or sets the Anime-Planet URL.</summary>
    [JsonPropertyName("animePlanet")]
    public string? AnimePlanet { get; set; }

    /// <summary>Gets or sets the AniDB URL.</summary>
    [JsonPropertyName("anidb")]
    public string? AniDb { get; set; }

    /// <summary>Gets or sets the streaming platforms.</summary>
    [JsonPropertyName("streams")]
    public IReadOnlyList<AnimeScheduleStreamEntry>? Streams { get; set; }
}

/// <summary>
/// One entry of a <c>GET /timetables/{airType}</c> response.
/// </summary>
public sealed class AnimeScheduleTimetableEntry
{
    /// <summary>Gets or sets the display title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the unique URL slug, matching <see cref="AnimeScheduleAnime.Route"/>.</summary>
    [JsonPropertyName("route")]
    public string? Route { get; set; }

    /// <summary>Gets or sets the romanized name.</summary>
    [JsonPropertyName("romaji")]
    public string? Romaji { get; set; }

    /// <summary>Gets or sets the English name.</summary>
    [JsonPropertyName("english")]
    public string? English { get; set; }

    /// <summary>Gets or sets the native-script name.</summary>
    [JsonPropertyName("native")]
    public string? Native { get; set; }

    /// <summary>Gets or sets the timetable's delayed display text.</summary>
    [JsonPropertyName("delayedText")]
    public string? DelayedText { get; set; }

    /// <summary>Gets or sets the date the episode was delayed from.</summary>
    [JsonPropertyName("delayedFrom")]
    public DateTimeOffset? DelayedFrom { get; set; }

    /// <summary>Gets or sets the date the episode was delayed until.</summary>
    [JsonPropertyName("delayedUntil")]
    public DateTimeOffset? DelayedUntil { get; set; }

    /// <summary>
    /// Gets or sets the airing status: <c>Finished</c>, <c>Ongoing</c>, <c>Delayed</c> or <c>Upcoming</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Gets or sets the episode's date and time.</summary>
    [JsonPropertyName("episodeDate")]
    public DateTimeOffset? EpisodeDate { get; set; }

    /// <summary>Gets or sets the episode number.</summary>
    [JsonPropertyName("episodeNumber")]
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the lowest episode number when several episodes air together.
    /// </summary>
    /// <remarks>
    /// Present only for multi-episode slots; the range then reads
    /// <c>subtractedEpisodeNumber - episodeNumber</c>.
    /// </remarks>
    [JsonPropertyName("subtractedEpisodeNumber")]
    public int? SubtractedEpisodeNumber { get; set; }

    /// <summary>Gets or sets the anime's total episode count; <c>0</c> means unknown.</summary>
    [JsonPropertyName("episodes")]
    public int? Episodes { get; set; }

    /// <summary>Gets or sets the episode length in minutes.</summary>
    [JsonPropertyName("lengthMin")]
    public int? LengthMin { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a donghua (Chinese animation).</summary>
    [JsonPropertyName("donghua")]
    public bool Donghua { get; set; }

    /// <summary>Gets or sets the air type: <c>raw</c>, <c>sub</c> or <c>dub</c>.</summary>
    [JsonPropertyName("airType")]
    public string? AirType { get; set; }

    /// <summary>Gets or sets the media types.</summary>
    [JsonPropertyName("mediaTypes")]
    public IReadOnlyList<AnimeScheduleCategory>? MediaTypes { get; set; }

    /// <summary>Gets or sets the poster/image URL slug.</summary>
    [JsonPropertyName("imageVersionRoute")]
    public string? ImageVersionRoute { get; set; }

    /// <summary>Gets or sets the streaming platforms carrying this episode.</summary>
    [JsonPropertyName("streams")]
    public IReadOnlyList<AnimeScheduleStreamEntry>? Streams { get; set; }

    /// <summary>
    /// Gets or sets the episode's immediate timetable status: <c>airing</c>, <c>aired</c>,
    /// <c>unaired</c> or <c>delayed-air</c>.
    /// </summary>
    [JsonPropertyName("airingStatus")]
    public string? AiringStatus { get; set; }
}
