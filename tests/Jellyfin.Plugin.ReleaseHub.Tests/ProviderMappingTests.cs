using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.ReleaseHub.Models;
using Jellyfin.Plugin.ReleaseHub.Providers.AnimeSchedule;
using Jellyfin.Plugin.ReleaseHub.Providers.TvMaze;
using Xunit;

namespace Jellyfin.Plugin.ReleaseHub.Tests;

/// <summary>
/// Verifies that provider responses map onto the normalized model correctly.
/// </summary>
/// <remarks>
/// Driven by captured JSON rather than live calls, so the suite runs offline, spends nobody's rate
/// limit, and fails when a mapper regresses rather than when the network is flaky.
/// </remarks>
public class ProviderMappingTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static T Load<T>(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new InvalidOperationException($"Fixture {fixture} deserialized to null.");
    }

    [Fact]
    public void TvMaze_Show_MapsIdentifiersJellyfinCanMatchOn()
    {
        var show = Load<TvMazeShow>("tvmaze-show.json");

        var series = TvMazeProvider.Map(show);

        Assert.Equal("Stranger Things", series.Title);
        Assert.Equal("2993", series.ProviderId);
        Assert.Equal(2016, series.Year);
        Assert.Equal("Netflix", series.StreamingPlatform);

        // These are what make an exact match against a Jellyfin library item possible.
        Assert.Equal("305288", series.ExternalIds[SeriesIdentity.Tvdb]);
        Assert.Equal("tt4574334", series.ExternalIds[SeriesIdentity.Imdb]);
    }

    [Fact]
    public void TvMaze_Summary_IsStrippedOfMarkup()
    {
        var show = Load<TvMazeShow>("tvmaze-show.json");

        var series = TvMazeProvider.Map(show);

        Assert.NotNull(series.Summary);
        Assert.DoesNotContain("<", series.Summary!, StringComparison.Ordinal);
        Assert.StartsWith("When a young boy vanishes", series.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TvMaze_EmptyAirTime_DoesNotProduceABroadcastTime()
    {
        // TVMaze still emits an airstamp when it has no airtime, synthesising midday UTC. Trusting it
        // would put a fabricated broadcast time on the calendar.
        var episodes = Load<List<TvMazeEpisode>>("tvmaze-episodes.json");
        var series = TvMazeProvider.Map(Load<TvMazeShow>("tvmaze-show.json"));

        var mapped = TvMazeProvider.MapEpisode(series, episodes[0], DateTime.UtcNow);

        Assert.NotNull(mapped);
        Assert.False(mapped!.HasReleaseTime);
        Assert.Equal(new DateTime(2016, 7, 15, 12, 0, 0, DateTimeKind.Utc), mapped.ReleaseUtc);
        Assert.Equal(DateCertainty.Exact, mapped.Certainty);
    }

    [Fact]
    public void TvMaze_RealAirTime_IsConvertedToUtcAndKept()
    {
        var episodes = Load<List<TvMazeEpisode>>("tvmaze-episodes.json");
        var series = TvMazeProvider.Map(Load<TvMazeShow>("tvmaze-show.json"));

        var mapped = TvMazeProvider.MapEpisode(series, episodes[1], DateTime.UtcNow);

        Assert.NotNull(mapped);
        Assert.True(mapped!.HasReleaseTime);

        // 21:00+02:00 is 19:00 UTC.
        Assert.Equal(new DateTime(2016, 7, 15, 19, 0, 0, DateTimeKind.Utc), mapped.ReleaseUtc);
    }

    [Fact]
    public void TvMaze_SpecialEpisode_IsClassifiedAsSpecial()
    {
        var episodes = Load<List<TvMazeEpisode>>("tvmaze-episodes.json");
        var series = TvMazeProvider.Map(Load<TvMazeShow>("tvmaze-show.json"));

        var mapped = TvMazeProvider.MapEpisode(series, episodes[2], DateTime.UtcNow);

        Assert.NotNull(mapped);
        Assert.Equal(ReleaseKind.Special, mapped!.Kind);
    }

    [Theory]
    [InlineData("Animation", "Japanese", true)]
    [InlineData("Animation", "English", false)]
    [InlineData("Scripted", "Japanese", false)]
    public void TvMaze_AnimeHeuristic_NeedsBothAnimationAndJapanese(string type, string language, bool expected)
    {
        var show = new TvMazeShow { Type = type, Language = language, Genres = [] };

        Assert.Equal(expected, TvMazeProvider.LooksLikeAnime(show));
    }

    [Fact]
    public void TvMaze_AnimeGenre_IsEnoughOnItsOwn()
    {
        var show = new TvMazeShow { Type = "Scripted", Language = "English", Genres = ["Anime"] };

        Assert.True(TvMazeProvider.LooksLikeAnime(show));
    }

    [Fact]
    public void AnimeSchedule_Timetable_MapsEpisodeAndAirType()
    {
        var entries = Load<List<AnimeScheduleTimetableEntry>>("animeschedule-timetable.json");

        var mapped = AnimeScheduleProvider.MapTimetableEntry(entries[0], DateTime.UtcNow);

        Assert.NotNull(mapped);
        Assert.Equal("one-piece", mapped!.ProviderId);
        Assert.Equal(1150, mapped.EpisodeNumber);
        Assert.Equal(SubDubKind.Sub, mapped.SubDub);
        Assert.True(mapped.IsAnime);
        Assert.True(mapped.HasReleaseTime);
        Assert.Equal("Crunchyroll", mapped.StreamingPlatform);

        // AnimeSchedule schedules per broadcast slot, so there is no meaningful season number to show.
        Assert.Null(mapped.SeasonNumber);
    }

    [Fact]
    public void AnimeSchedule_NativeTitle_BecomesTheOriginalTitle()
    {
        var entries = Load<List<AnimeScheduleTimetableEntry>>("animeschedule-timetable.json");

        var mapped = AnimeScheduleProvider.MapTimetableEntry(entries[0], DateTime.UtcNow);

        Assert.Equal("ワンピース", mapped!.OriginalTitle);
    }

    [Fact]
    public void AnimeSchedule_DelayedEpisode_IsNotPresentedAsConfirmed()
    {
        var entries = Load<List<AnimeScheduleTimetableEntry>>("animeschedule-timetable.json");

        var mapped = AnimeScheduleProvider.MapTimetableEntry(entries[1], DateTime.UtcNow);

        Assert.NotNull(mapped);
        Assert.Equal(DateCertainty.Approximate, mapped!.Certainty);
        Assert.Equal("Delayed one week", mapped.ApproximateLabel);
        Assert.Equal(SubDubKind.Dub, mapped.SubDub);
    }

    [Fact]
    public void AnimeSchedule_MultiEpisodeSlot_IsFormattedAsARange()
    {
        var entries = Load<List<AnimeScheduleTimetableEntry>>("animeschedule-timetable.json");

        Assert.Equal("7-12", AnimeScheduleProvider.FormatEpisodeRange(entries[1]));
        Assert.Null(AnimeScheduleProvider.FormatEpisodeRange(entries[0]));
    }

    [Fact]
    public void AnimeSchedule_NullDateSentinel_IsRejected()
    {
        // The API uses 0001-01-01T00:00:00Z as its null date. Treating it as real would park every
        // unscheduled anime at the very start of the calendar.
        var entries = Load<List<AnimeScheduleTimetableEntry>>("animeschedule-timetable.json");

        Assert.Null(AnimeScheduleProvider.MapTimetableEntry(entries[2], DateTime.UtcNow));
    }

    [Theory]
    [InlineData("sub", SubDubKind.Sub)]
    [InlineData("DUB", SubDubKind.Dub)]
    [InlineData("raw", SubDubKind.Raw)]
    [InlineData("", SubDubKind.Unknown)]
    [InlineData(null, SubDubKind.Unknown)]
    public void AnimeSchedule_AirType_ParsesCaseInsensitively(string? airType, SubDubKind expected)
    {
        Assert.Equal(expected, AnimeScheduleProvider.ParseAirType(airType));
    }

    [Fact]
    public void AnimeSchedule_WebsiteLinks_YieldAniListAndAniDbIdentifiers()
    {
        // AnimeSchedule publishes URLs, not bare ids; these are the only reliable way to match anime
        // against a Jellyfin library, since anime titles differ wildly between databases.
        var websites = new AnimeScheduleWebsites
        {
            AniList = "https://anilist.co/anime/21",
            AniDb = "https://anidb.net/anime/69",
            MyAnimeList = "https://myanimelist.net/anime/21/One_Piece"
        };

        var ids = AnimeScheduleProvider.ExtractExternalIds(websites);

        Assert.Equal("21", ids[SeriesIdentity.AniList]);
        Assert.Equal("69", ids[SeriesIdentity.AniDb]);
    }

    [Fact]
    public void AnimeSchedule_FilmIsNeverAPlausibleSeries()
    {
        // Jellyfin stores AniDB 411 on the One Piece *series*, but 411 is the 2000 film; the TV run
        // is AniDB 69. Trusting the identifier alone mapped the series to a film with no broadcast
        // schedule and silently emptied its calendar.
        var film = new AnimeScheduleAnime
        {
            Route = "one-piece-movie",
            MediaTypes = [new AnimeScheduleCategory { Name = "Movie", Route = "movie" }]
        };

        Assert.False(AnimeScheduleProvider.IsPlausibleSeries(film));
    }

    [Theory]
    [InlineData("TV")]
    [InlineData("ONA")]
    [InlineData("OVA")]
    [InlineData("Special")]
    [InlineData("TV Short")]
    public void AnimeSchedule_EpisodicMediaTypesStayPlausible(string mediaType)
    {
        // Only films are excluded. These are all legitimate ways an episodic release is catalogued,
        // and rejecting them would lose real series.
        var anime = new AnimeScheduleAnime
        {
            Route = "something",
            MediaTypes = [new AnimeScheduleCategory { Name = mediaType }]
        };

        Assert.True(AnimeScheduleProvider.IsPlausibleSeries(anime));
    }

    [Fact]
    public void AnimeSchedule_AbsentMediaTypesAreNotEvidence()
    {
        // Missing data must not be read as "this is a film".
        Assert.True(AnimeScheduleProvider.IsPlausibleSeries(new AnimeScheduleAnime { Route = "x" }));
        Assert.True(AnimeScheduleProvider.IsPlausibleSeries(
            new AnimeScheduleAnime { Route = "x", MediaTypes = [] }));
    }

    [Fact]
    public void AnimeSchedule_MixedMediaTypesStayPlausible()
    {
        var anime = new AnimeScheduleAnime
        {
            Route = "x",
            MediaTypes =
            [
                new AnimeScheduleCategory { Name = "Movie" },
                new AnimeScheduleCategory { Name = "TV" }
            ]
        };

        Assert.True(AnimeScheduleProvider.IsPlausibleSeries(anime));
    }

    [Fact]
    public void AnimeSchedule_MissingWebsites_YieldNoIdentifiers()
    {
        Assert.Empty(AnimeScheduleProvider.ExtractExternalIds(null));
        Assert.Empty(AnimeScheduleProvider.ExtractExternalIds(new AnimeScheduleWebsites()));
    }

    [Fact]
    public void AnimeSchedule_Streams_ReadPlatformsFromTheArray()
    {
        // API 1.1 replaced fixed per-platform fields with a flexible array; reading a hardcoded list
        // of property names would silently show no platform at all.
        var streams = new List<AnimeScheduleStreamEntry>
        {
            new() { Platform = "crunchyroll", Name = "Crunchyroll (Raw)" }
        };

        Assert.Equal("Crunchyroll (Raw)", AnimeScheduleProvider.FirstStreamingService(streams));
        Assert.Null(AnimeScheduleProvider.FirstStreamingService(null));
        Assert.Null(AnimeScheduleProvider.FirstStreamingService([]));
    }
}
