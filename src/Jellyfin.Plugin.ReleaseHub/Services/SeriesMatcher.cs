using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ReleaseHub.Models;

namespace Jellyfin.Plugin.ReleaseHub.Services;

/// <summary>
/// Scores provider candidates against what Jellyfin knows about a series.
/// </summary>
/// <remarks>
/// Used only after identifier lookups have failed. The scoring is intentionally cautious: a confident
/// wrong match fills a user's calendar with a different show and is much worse than a series that shows
/// nothing until someone confirms the match by hand.
/// </remarks>
public static class SeriesMatcher
{
    /// <summary>Similarity at or above which two titles are treated as the same wording.</summary>
    private const double StrongSimilarity = 0.92;

    /// <summary>Similarity below which a candidate is discarded outright.</summary>
    private const double MinimumSimilarity = 0.72;

    /// <summary>
    /// Picks the best candidate for an identity.
    /// </summary>
    /// <param name="identity">What Jellyfin knows about the series.</param>
    /// <param name="candidates">Candidates returned by a provider search.</param>
    /// <returns>The best match, or <see langword="null"/> when nothing is plausible.</returns>
    public static ProviderMatch? PickBest(SeriesIdentity identity, IReadOnlyList<ProviderSeries> candidates)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(candidates);

        ProviderMatch? best = null;
        var bestScore = 0.0;

        foreach (var candidate in candidates)
        {
            var scored = Score(identity, candidate);
            if (scored is null)
            {
                continue;
            }

            var (match, score) = scored.Value;
            if (score > bestScore)
            {
                best = match;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// Scores a single candidate.
    /// </summary>
    /// <param name="identity">What Jellyfin knows about the series.</param>
    /// <param name="candidate">The provider candidate.</param>
    /// <returns>The match and its raw score, or <see langword="null"/> when implausible.</returns>
    internal static (ProviderMatch Match, double Score)? Score(SeriesIdentity identity, ProviderSeries candidate)
    {
        // Cross-check identifiers first: a provider search result can still carry an id that matches,
        // which settles the question without any title guesswork.
        foreach (var (key, value) in candidate.ExternalIds)
        {
            var known = identity.GetProviderId(key);
            if (known is not null && string.Equals(known, value, StringComparison.OrdinalIgnoreCase))
            {
                return (new ProviderMatch(candidate, MatchConfidence.Exact, $"{key} id match"), 1.0);
            }
        }

        var (similarity, source) = BestTitleSimilarity(identity, candidate);
        if (similarity < MinimumSimilarity)
        {
            return null;
        }

        var yearAgreement = CompareYears(identity.Year, candidate.Year);
        var confidence = Classify(similarity, yearAgreement);
        if (confidence == MatchConfidence.None)
        {
            return null;
        }

        var reason = $"{source} similarity {similarity:0.00}, {Describe(yearAgreement)}";

        // Year agreement contributes to ordering as well as to confidence, so that among several
        // same-titled candidates the one from the right era wins.
        var score = similarity + yearAgreement switch
        {
            YearAgreement.Same => 0.30,
            YearAgreement.Adjacent => 0.10,
            YearAgreement.Unknown => 0.0,
            _ => -0.40
        };

        return (new ProviderMatch(candidate, confidence, reason), score);
    }

    private static (double Similarity, string Source) BestTitleSimilarity(
        SeriesIdentity identity,
        ProviderSeries candidate)
    {
        var jellyfinTitles = new List<(string Value, string Source)>(2)
        {
            (TextUtil.NormalizeTitle(identity.Title), "title")
        };

        if (!string.IsNullOrWhiteSpace(identity.OriginalTitle))
        {
            jellyfinTitles.Add((TextUtil.NormalizeTitle(identity.OriginalTitle), "original title"));
        }

        var providerTitles = new List<string>(2 + candidate.AlternateTitles.Count)
        {
            TextUtil.NormalizeTitle(candidate.Title)
        };

        if (!string.IsNullOrWhiteSpace(candidate.OriginalTitle))
        {
            providerTitles.Add(TextUtil.NormalizeTitle(candidate.OriginalTitle));
        }

        foreach (var alternate in candidate.AlternateTitles)
        {
            providerTitles.Add(TextUtil.NormalizeTitle(alternate));
        }

        var best = 0.0;
        var bestSource = "title";

        foreach (var (value, source) in jellyfinTitles)
        {
            foreach (var providerTitle in providerTitles)
            {
                var similarity = TextUtil.Similarity(value, providerTitle);
                if (similarity > best)
                {
                    best = similarity;
                    bestSource = source;
                }
            }
        }

        return (best, bestSource);
    }

    private static MatchConfidence Classify(double similarity, YearAgreement year)
    {
        // A same-titled show from a clearly different era is usually a remake, not the same series.
        // Refusing to auto-accept it is the whole point of tracking confidence.
        if (year == YearAgreement.Different)
        {
            return similarity >= StrongSimilarity ? MatchConfidence.Low : MatchConfidence.None;
        }

        if (similarity >= StrongSimilarity)
        {
            return year switch
            {
                YearAgreement.Same => MatchConfidence.High,
                YearAgreement.Adjacent => MatchConfidence.High,
                _ => MatchConfidence.Medium
            };
        }

        return year == YearAgreement.Same ? MatchConfidence.Medium : MatchConfidence.Low;
    }

    private static YearAgreement CompareYears(int? left, int? right)
    {
        if (left is not { } a || right is not { } b)
        {
            return YearAgreement.Unknown;
        }

        var difference = Math.Abs(a - b);
        return difference switch
        {
            0 => YearAgreement.Same,

            // A one-year gap is routine: Jellyfin often records a local premiere while the provider
            // records the original broadcast, and those straddle a new year often enough to matter.
            1 => YearAgreement.Adjacent,
            _ => YearAgreement.Different
        };
    }

    private static string Describe(YearAgreement agreement) => agreement switch
    {
        YearAgreement.Same => "year matches",
        YearAgreement.Adjacent => "year within one",
        YearAgreement.Different => "year differs",
        _ => "year unknown"
    };

    private enum YearAgreement
    {
        Unknown,
        Same,
        Adjacent,
        Different
    }
}
