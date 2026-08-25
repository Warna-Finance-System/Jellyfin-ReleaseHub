using System;
using Jellyfin.Plugin.ReleaseHub.Services;
using Xunit;

namespace Jellyfin.Plugin.ReleaseHub.Tests;

/// <summary>
/// Verifies that the synchronization window is trimmed per provider.
/// </summary>
/// <remarks>
/// The horizon is chosen for what a reader wants to see, which is not the same question as what each
/// provider charges to answer. A provider billed per week of window must not have its traffic
/// multiplied just because the horizon was widened for providers it costs nothing to ask.
/// </remarks>
public class SyncWindowTests
{
    private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NoLimit_KeepsTheWholeWindow()
    {
        var to = From.AddDays(365);

        Assert.Equal(to, ReleaseService.WindowEndFor(null, From, to));
    }

    [Fact]
    public void LimitShorterThanWindow_TrimsToTheLimit()
    {
        var to = From.AddDays(365);

        var end = ReleaseService.WindowEndFor(TimeSpan.FromDays(182), From, to);

        Assert.Equal(From.AddDays(182), end);
    }

    [Fact]
    public void LimitLongerThanWindow_LeavesTheWindowAlone()
    {
        // A provider must never be asked past the horizon just because it would tolerate it: nothing
        // beyond the horizon is ever stored, so the extra requests would be spent for nothing.
        var to = From.AddDays(30);

        var end = ReleaseService.WindowEndFor(TimeSpan.FromDays(182), From, to);

        Assert.Equal(to, end);
    }

    [Fact]
    public void LimitEqualToWindow_LeavesTheWindowAlone()
    {
        var to = From.AddDays(182);

        var end = ReleaseService.WindowEndFor(TimeSpan.FromDays(182), From, to);

        Assert.Equal(to, end);
    }

    [Fact]
    public void HorizonIsPublishedForTheClient()
    {
        // The interface stops offering to load more at this point, so it has to be a real number and
        // not a placeholder the client would have to hardcode.
        Assert.Equal(365, ReleaseService.HorizonDays);
    }
}
