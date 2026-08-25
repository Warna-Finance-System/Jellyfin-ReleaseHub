using System;
using Jellyfin.Plugin.ReleaseHub.Models;
using Jellyfin.Plugin.ReleaseHub.Providers.Tmdb;
using Xunit;

namespace Jellyfin.Plugin.ReleaseHub.Tests;

/// <summary>
/// Verifies how TMDb films map onto the calendar.
/// </summary>
/// <remarks>
/// The distinction these tests protect is that a film's release is a date, not a broadcast slot, and
/// that the date is often provisional. Flattening either of those into a plain timestamp would present
/// a guess as a schedule.
/// </remarks>
public class TmdbMappingTests
{
    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Passed explicitly so the mappers stay pure and need no plugin configuration.</summary>
    private const string ImageRoot = "https://image.tmdb.org/t/p/w500";

    [Fact]
    public void ReleasedFilm_ProducesNoUpcomingEntry()
    {
        var movie = new TmdbMovie { Id = 1, Title = "Old Film", ReleaseDate = "2020-01-01", Status = "Released" };

        Assert.Null(TmdbProvider.MapRelease(movie, Now, ImageRoot));
    }

    [Fact]
    public void FinishedFilmWithADate_IsExact()
    {
        var movie = new TmdbMovie
        {
            Id = 2,
            Title = "Coming Soon",
            ReleaseDate = "2026-12-18",
            Status = "Post Production"
        };

        var release = TmdbProvider.MapRelease(movie, Now, ImageRoot);

        Assert.NotNull(release);
        Assert.Equal(DateCertainty.Exact, release!.Certainty);
        Assert.Equal(ReleaseKind.Movie, release.Kind);

        // A film has a release date, never a showtime; rendering midnight would invent one.
        Assert.False(release.HasReleaseTime);
        Assert.Null(release.SeasonNumber);
        Assert.Null(release.EpisodeNumber);
    }

    [Fact]
    public void UnfinishedFilmWithADate_IsOnlyApproximate()
    {
        // Dates for films still in production slip routinely, so they must not read as settled.
        var movie = new TmdbMovie
        {
            Id = 3,
            Title = "Filming Now",
            ReleaseDate = "2027-06-04",
            Status = "In Production"
        };

        var release = TmdbProvider.MapRelease(movie, Now, ImageRoot);

        Assert.NotNull(release);
        Assert.Equal(DateCertainty.Approximate, release!.Certainty);
    }

    [Fact]
    public void AnnouncedFilmWithoutADate_IsStillSurfaced()
    {
        // "The next one exists but is undated" is exactly what someone following a saga wants to know.
        var movie = new TmdbMovie { Id = 4, Title = "Untitled Sequel", ReleaseDate = "", Status = "Planned" };

        var release = TmdbProvider.MapRelease(movie, Now, ImageRoot);

        Assert.NotNull(release);
        Assert.Equal(DateCertainty.AnnouncedNoDate, release!.Certainty);
        Assert.Null(release.ReleaseUtc);
    }

    [Fact]
    public void UndatedFilm_SurvivesWindowFilteringAnyway()
    {
        var undated = new ReleaseItem { Certainty = DateCertainty.AnnouncedNoDate, ReleaseUtc = null };

        Assert.True(TmdbProvider.InWindow(undated, Now, Now.AddDays(30)));
    }

    [Fact]
    public void DatedFilmOutsideTheWindow_IsFilteredOut()
    {
        var far = new ReleaseItem { Certainty = DateCertainty.Exact, ReleaseUtc = Now.AddYears(3) };

        Assert.False(TmdbProvider.InWindow(far, Now, Now.AddDays(30)));
    }

    [Theory]
    [InlineData("Released", true)]
    [InlineData("Post Production", true)]
    [InlineData("In Production", false)]
    [InlineData("Planned", false)]
    [InlineData(null, false)]
    public void DateFirmness_FollowsProductionStatus(string? status, bool expected)
    {
        Assert.Equal(expected, TmdbProvider.IsDateFirm(status));
    }

    [Fact]
    public void Map_CarriesTheIdentifiersJellyfinCanMatchOn()
    {
        var movie = new TmdbMovie
        {
            Id = 603,
            Title = "The Matrix",
            OriginalTitle = "The Matrix",
            ReleaseDate = "1999-03-30",
            ImdbId = "tt0133093"
        };

        var series = TmdbProvider.Map(movie, ImageRoot);

        Assert.Equal("603", series.ProviderId);
        Assert.Equal(1999, series.Year);
        Assert.Equal("603", series.ExternalIds[SeriesIdentity.Tmdb]);
        Assert.Equal("tt0133093", series.ExternalIds[SeriesIdentity.Imdb]);
        Assert.False(series.IsAnime);
    }

    [Fact]
    public void ImageUrl_JoinsTheCdnRootWithoutDoublingTheSlash()
    {
        // TMDb returns paths that already begin with a slash.
        Assert.Equal("https://image.tmdb.org/t/p/w500/abc.jpg", TmdbProvider.BuildImageUrl("/abc.jpg", ImageRoot));
        Assert.Null(TmdbProvider.BuildImageUrl(null, ImageRoot));
        Assert.Null(TmdbProvider.BuildImageUrl("", ImageRoot));
    }
}
