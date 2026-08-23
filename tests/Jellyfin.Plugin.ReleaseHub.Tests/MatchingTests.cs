using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ReleaseHub.Models;
using Jellyfin.Plugin.ReleaseHub.Services;
using Xunit;

namespace Jellyfin.Plugin.ReleaseHub.Tests;

/// <summary>
/// Verifies how ReleaseHub decides that a Jellyfin series and a provider record are the same show.
/// </summary>
/// <remarks>
/// The behaviour under test is deliberately cautious. A confident wrong match fills someone's calendar
/// with a different show's episodes, which is far worse than a series that shows nothing until it is
/// confirmed by hand, so most of these tests assert that a plausible-looking match is *not* accepted.
/// </remarks>
public class MatchingTests
{
    private static SeriesIdentity Identity(
        string title,
        int? year = null,
        string? originalTitle = null,
        params (string Key, string Value)[] ids)
    {
        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ids)
        {
            providerIds[key] = value;
        }

        return new SeriesIdentity
        {
            JellyfinItemId = Guid.NewGuid(),
            Title = title,
            OriginalTitle = originalTitle,
            Year = year,
            ProviderIds = providerIds
        };
    }

    private static ProviderSeries Candidate(
        string title,
        int? year = null,
        IReadOnlyList<string>? alternates = null,
        params (string Key, string Value)[] ids)
    {
        var externalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ids)
        {
            externalIds[key] = value;
        }

        return new ProviderSeries
        {
            Provider = ReleaseProviderKind.TvMaze,
            ProviderId = "1",
            Title = title,
            Year = year,
            AlternateTitles = alternates ?? Array.Empty<string>(),
            ExternalIds = externalIds
        };
    }

    [Fact]
    public void IdentifierAgreement_BeatsEverythingElse()
    {
        // Titles disagree completely; the shared TVDB id settles it anyway.
        var identity = Identity("Totally Different Name", 1999, null, (SeriesIdentity.Tvdb, "305288"));
        var candidate = Candidate("Stranger Things", 2016, null, (SeriesIdentity.Tvdb, "305288"));

        var match = SeriesMatcher.PickBest(identity, [candidate]);

        Assert.NotNull(match);
        Assert.Equal(MatchConfidence.Exact, match!.Confidence);
        Assert.True(match.IsAutoAcceptable);
    }

    [Fact]
    public void ExactTitleAndYear_IsAcceptedAutomatically()
    {
        var match = SeriesMatcher.PickBest(
            Identity("Stranger Things", 2016),
            [Candidate("Stranger Things", 2016)]);

        Assert.NotNull(match);
        Assert.Equal(MatchConfidence.High, match!.Confidence);
        Assert.True(match.IsAutoAcceptable);
    }

    [Fact]
    public void SameTitleDifferentEra_IsNotAcceptedAutomatically()
    {
        // Almost always a remake. Auto-accepting it would silently show the wrong show's schedule.
        var match = SeriesMatcher.PickBest(
            Identity("Battlestar Galactica", 2004),
            [Candidate("Battlestar Galactica", 1978)]);

        Assert.NotNull(match);
        Assert.Equal(MatchConfidence.Low, match!.Confidence);
        Assert.False(match.IsAutoAcceptable);
    }

    [Fact]
    public void ExactTitleWithoutAnyYear_StaysBelowTheAutoAcceptThreshold()
    {
        var match = SeriesMatcher.PickBest(
            Identity("The Office"),
            [Candidate("The Office")]);

        Assert.NotNull(match);
        Assert.Equal(MatchConfidence.Medium, match!.Confidence);
        Assert.False(match.IsAutoAcceptable);
    }

    [Fact]
    public void OneYearApart_IsTreatedAsAgreement()
    {
        // Routine: Jellyfin often records a local premiere while the provider records the original
        // broadcast, and those straddle a new year often enough to matter.
        var match = SeriesMatcher.PickBest(
            Identity("Frieren", 2023),
            [Candidate("Frieren", 2024)]);

        Assert.NotNull(match);
        Assert.Equal(MatchConfidence.High, match!.Confidence);
    }

    [Fact]
    public void UnrelatedTitles_ProduceNoMatchAtAll()
    {
        var match = SeriesMatcher.PickBest(
            Identity("Breaking Bad", 2008),
            [Candidate("Peppa Pig", 2004)]);

        Assert.Null(match);
    }

    [Fact]
    public void AlternateTitle_CanCarryTheMatch()
    {
        // Anime routinely differ between romaji, English and native titles across databases.
        var match = SeriesMatcher.PickBest(
            Identity("Frieren: Beyond Journey's End", 2023),
            [Candidate("Sousou no Frieren", 2023, ["Frieren: Beyond Journey's End"])]);

        Assert.NotNull(match);
        Assert.Equal(MatchConfidence.High, match!.Confidence);
    }

    [Fact]
    public void OriginalTitle_IsAlsoCompared()
    {
        var identity = Identity("Attack on Titan", 2013, "Shingeki no Kyojin");

        var match = SeriesMatcher.PickBest(identity, [Candidate("Shingeki no Kyojin", 2013)]);

        Assert.NotNull(match);
        Assert.Equal(MatchConfidence.High, match!.Confidence);
    }

    [Fact]
    public void BestCandidateWins_WhenSeveralShareATitle()
    {
        var identity = Identity("Doctor Who", 2005);

        var match = SeriesMatcher.PickBest(identity,
        [
            Candidate("Doctor Who", 1963),
            Candidate("Doctor Who", 2005),
            Candidate("Doctor Who Confidential", 2005)
        ]);

        Assert.NotNull(match);
        Assert.Equal(2005, match!.Series.Year);
        Assert.Equal(MatchConfidence.High, match.Confidence);
    }

    [Theory]
    [InlineData("Stranger Things (2016)", "stranger things")]
    [InlineData("  Stranger   Things  ", "stranger things")]
    [InlineData("Pokémon", "pokemon")]
    [InlineData("Marvel's Daredevil", "marvel s daredevil")]
    [InlineData("Re:ZERO -Starting Life-", "re zero starting life")]
    public void TitleNormalization_FoldsTheDifferencesThatDoNotMatter(string input, string expected)
    {
        Assert.Equal(expected, TextUtil.NormalizeTitle(input));
    }

    [Fact]
    public void TitleNormalization_KeepsArticlesAndSubtitles()
    {
        // "The Office" and "Office" are genuinely different shows; over-normalizing here is what
        // produces confident wrong matches.
        Assert.NotEqual(TextUtil.NormalizeTitle("The Office"), TextUtil.NormalizeTitle("Office"));
    }

    [Fact]
    public void HtmlStripping_HandlesEntitiesAndEmptyInput()
    {
        Assert.Equal("Tom & Jerry", TextUtil.StripHtml("<p>Tom &amp; Jerry</p>"));
        Assert.Null(TextUtil.StripHtml(null));
        Assert.Null(TextUtil.StripHtml("   "));
        Assert.Null(TextUtil.StripHtml("<p></p>"));
    }
}
