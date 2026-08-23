using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ReleaseHub.Models;
using Jellyfin.Plugin.ReleaseHub.Services;
using Xunit;

namespace Jellyfin.Plugin.ReleaseHub.Tests;

/// <summary>
/// Verifies the throttling and de-duplication behaviour that keeps ReleaseHub inside provider limits.
/// </summary>
public class RateLimiterTests
{
    [Fact]
    public async Task WithinBudget_RequestsAreNotDelayed()
    {
        using var limiter = new RateLimiter(permits: 5, window: TimeSpan.FromSeconds(10));
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < 5; i++)
        {
            await limiter.WaitAsync(CancellationToken.None);
        }

        stopwatch.Stop();
        Assert.True(
            stopwatch.ElapsedMilliseconds < 250,
            $"Five permits in a five-permit window should not block, took {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task ExceedingBudget_BlocksUntilTheWindowSlides()
    {
        using var limiter = new RateLimiter(permits: 2, window: TimeSpan.FromMilliseconds(400));

        await limiter.WaitAsync(CancellationToken.None);
        await limiter.WaitAsync(CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await limiter.WaitAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds >= 300,
            $"The third permit should have waited for the window, took only {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task CoolOff_IsHonouredEvenWithBudgetRemaining()
    {
        // A 429 with Retry-After outranks the local window: the provider has said exactly how long to
        // stay away, and going back sooner would be a deliberate limit bypass.
        using var limiter = new RateLimiter(permits: 100, window: TimeSpan.FromSeconds(60));
        limiter.ApplyCoolOff(TimeSpan.FromMilliseconds(400));

        var stopwatch = Stopwatch.StartNew();
        await limiter.WaitAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds >= 300,
            $"The cool-off should have been honoured, waited only {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public void CoolOff_OnlyEverExtends()
    {
        using var limiter = new RateLimiter(permits: 10, window: TimeSpan.FromSeconds(10));

        limiter.ApplyCoolOff(TimeSpan.FromMinutes(5));
        var longCoolOff = limiter.CoolOffUntil;

        // A later response asking for a shorter wait must not shorten one already in effect.
        limiter.ApplyCoolOff(TimeSpan.FromSeconds(1));

        Assert.Equal(longCoolOff, limiter.CoolOffUntil);
    }

    [Fact]
    public void CoolOff_IgnoresNonPositiveDurations()
    {
        using var limiter = new RateLimiter(permits: 10, window: TimeSpan.FromSeconds(10));

        limiter.ApplyCoolOff(TimeSpan.Zero);
        limiter.ApplyCoolOff(TimeSpan.FromSeconds(-30));

        Assert.Equal(DateTimeOffset.MinValue, limiter.CoolOffUntil);
    }

    [Fact]
    public async Task Cancellation_IsObservedWhileWaiting()
    {
        using var limiter = new RateLimiter(permits: 1, window: TimeSpan.FromSeconds(30));
        await limiter.WaitAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.WaitAsync(cts.Token));
    }

    [Fact]
    public void Constructor_RejectsNonsensicalBudgets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimiter(0, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimiter(1, TimeSpan.Zero));
    }
}

/// <summary>
/// Verifies that releases reported by more than one provider collapse into one calendar entry.
/// </summary>
public class DeduplicationTests
{
    private static ReleaseItem Release(
        ReleaseProviderKind provider,
        string title,
        DateTime releaseUtc,
        bool hasTime,
        SubDubKind subDub = SubDubKind.Unknown,
        int? episode = 1)
        => new()
        {
            Provider = provider,
            ProviderId = provider.ToString(),
            Title = title,
            EpisodeNumber = episode,
            ReleaseUtc = releaseUtc,
            HasReleaseTime = hasTime,
            SubDub = subDub,
            Certainty = DateCertainty.Exact
        };

    [Fact]
    public void SameEpisodeFromBothProviders_CollapsesToOneEntry()
    {
        var when = new DateTime(2026, 8, 22, 19, 30, 0, DateTimeKind.Utc);

        var result = ReleaseService.Deduplicate(
        [
            Release(ReleaseProviderKind.TvMaze, "One Piece", when, hasTime: false),
            Release(ReleaseProviderKind.AnimeSchedule, "One Piece", when, hasTime: true, SubDubKind.Sub)
        ]);

        Assert.Single(result);
    }

    [Fact]
    public void TheRicherRecordSurvivesDeduplication()
    {
        // AnimeSchedule knows the real airtime and the sub/dub variant; TVMaze usually does not.
        // Keeping the poorer record would lose information the user can see.
        var when = new DateTime(2026, 8, 22, 19, 30, 0, DateTimeKind.Utc);

        var result = ReleaseService.Deduplicate(
        [
            Release(ReleaseProviderKind.TvMaze, "One Piece", when, hasTime: false),
            Release(ReleaseProviderKind.AnimeSchedule, "One Piece", when, hasTime: true, SubDubKind.Sub)
        ]);

        Assert.Equal(ReleaseProviderKind.AnimeSchedule, result[0].Provider);
        Assert.True(result[0].HasReleaseTime);
        Assert.Equal(SubDubKind.Sub, result[0].SubDub);
    }

    [Fact]
    public void SubAndDubOfTheSameEpisode_AreKeptSeparate()
    {
        // They are genuinely two different broadcasts and a viewer may care about only one of them.
        var when = new DateTime(2026, 8, 22, 19, 30, 0, DateTimeKind.Utc);

        var result = ReleaseService.Deduplicate(
        [
            Release(ReleaseProviderKind.AnimeSchedule, "One Piece", when, true, SubDubKind.Sub),
            Release(ReleaseProviderKind.AnimeSchedule, "One Piece", when, true, SubDubKind.Dub)
        ]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DifferentEpisodes_AreNeverCollapsed()
    {
        var when = new DateTime(2026, 8, 22, 19, 30, 0, DateTimeKind.Utc);

        var result = ReleaseService.Deduplicate(
        [
            Release(ReleaseProviderKind.TvMaze, "Show", when, true, episode: 1),
            Release(ReleaseProviderKind.TvMaze, "Show", when, true, episode: 2)
        ]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ResultsComeBackInChronologicalOrder()
    {
        var early = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);
        var late = new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);

        var result = ReleaseService.Deduplicate(
        [
            Release(ReleaseProviderKind.TvMaze, "Later Show", late, true),
            Release(ReleaseProviderKind.TvMaze, "Earlier Show", early, true)
        ]);

        Assert.Equal("Earlier Show", result[0].Title);
        Assert.Equal("Later Show", result[1].Title);
    }

    [Fact]
    public void EmptyInput_YieldsEmptyOutput()
    {
        Assert.Empty(ReleaseService.Deduplicate([]));
    }

    [Fact]
    public void CacheKey_SeparatesVariantsThatDisplayMerges()
    {
        // Storage and display need different keys: the cache must keep a sub and a dub airing as two
        // rows, while the calendar merges an unlabelled TVMaze record into whichever one it duplicates.
        var when = new DateTime(2026, 8, 22, 19, 30, 0, DateTimeKind.Utc);
        var sub = Release(ReleaseProviderKind.AnimeSchedule, "One Piece", when, true, SubDubKind.Sub);
        var dub = Release(ReleaseProviderKind.AnimeSchedule, "One Piece", when, true, SubDubKind.Dub);

        Assert.Equal(sub.GetDeduplicationKey(), dub.GetDeduplicationKey());
        Assert.NotEqual(sub.GetCacheKey(), dub.GetCacheKey());
    }
}
