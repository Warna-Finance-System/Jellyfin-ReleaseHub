namespace Jellyfin.Plugin.ReleaseHub.Models;

/// <summary>
/// Identifies which external provider a piece of release data came from.
/// </summary>
public enum ReleaseProviderKind
{
    /// <summary>No provider, or an unrecognized one.</summary>
    None = 0,

    /// <summary>TVMaze, used for standard TV series.</summary>
    TvMaze = 1,

    /// <summary>AnimeSchedule, used for anime broadcast schedules.</summary>
    AnimeSchedule = 2,

    /// <summary>TMDb, used for films and their collections.</summary>
    Tmdb = 3
}

/// <summary>
/// What kind of thing is being released.
/// </summary>
public enum ReleaseKind
{
    /// <summary>A single episode.</summary>
    Episode = 0,

    /// <summary>The premiere of a season, where the provider distinguishes it.</summary>
    SeasonPremiere = 1,

    /// <summary>A whole season announced without per-episode detail.</summary>
    Season = 2,

    /// <summary>A special or out-of-band episode.</summary>
    Special = 3,

    /// <summary>A feature film. Has no season or episode of its own.</summary>
    Movie = 4
}

/// <summary>
/// How much the release date can be trusted.
/// </summary>
/// <remarks>
/// The UI must never present an uncertain date as confirmed, so this distinction is carried all the way
/// from the provider mapper to the rendered card rather than being flattened into a nullable date.
/// </remarks>
public enum DateCertainty
{
    /// <summary>No date information at all.</summary>
    Unknown = 0,

    /// <summary>The item is announced but no date has been given.</summary>
    AnnouncedNoDate = 1,

    /// <summary>Only a coarse date is known, such as a year, quarter or month.</summary>
    Approximate = 2,

    /// <summary>An exact calendar date is confirmed, though possibly without a time.</summary>
    Exact = 3
}

/// <summary>
/// Audio/subtitle variant of an anime broadcast.
/// </summary>
public enum SubDubKind
{
    /// <summary>The provider did not say.</summary>
    Unknown = 0,

    /// <summary>Subtitled release.</summary>
    Sub = 1,

    /// <summary>Dubbed release.</summary>
    Dub = 2,

    /// <summary>Untranslated original broadcast.</summary>
    Raw = 3
}

/// <summary>
/// How confident the resolver is that a Jellyfin item and a provider record are the same show.
/// </summary>
/// <remarks>
/// Anything below <see cref="High"/> is never persisted as an automatic association; it is offered to the
/// user as a suggestion instead, so a wrong guess cannot silently poison the calendar.
/// </remarks>
public enum MatchConfidence
{
    /// <summary>No plausible match.</summary>
    None = 0,

    /// <summary>A weak, fuzzy match that must be confirmed by a human.</summary>
    Low = 1,

    /// <summary>A plausible match, for example a title match without a corroborating year.</summary>
    Medium = 2,

    /// <summary>A strong match, for example an exact title plus year.</summary>
    High = 3,

    /// <summary>An identifier match; the two records are certainly the same show.</summary>
    Exact = 4
}
