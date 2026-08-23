using System;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ReleaseHub.Services;

/// <summary>
/// Text helpers shared by the provider mappers and the resolver.
/// </summary>
public static partial class TextUtil
{
    /// <summary>
    /// Converts a provider's HTML summary into plain text.
    /// </summary>
    /// <param name="html">The HTML fragment, which may be <see langword="null"/>.</param>
    /// <returns>Plain text, or <see langword="null"/> when there was nothing to convert.</returns>
    /// <remarks>
    /// TVMaze summaries arrive wrapped in <c>&lt;p&gt;</c> tags. The frontend inserts summaries as text
    /// rather than markup, so stripping here keeps tags from being displayed literally — and means no
    /// provider-supplied markup can ever reach the DOM.
    /// </remarks>
    public static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var withoutTags = HtmlTagRegex().Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var collapsed = WhitespaceRegex().Replace(decoded, " ").Trim();

        return collapsed.Length == 0 ? null : collapsed;
    }

    /// <summary>
    /// Normalizes a title so two spellings of the same show compare equal.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <returns>A comparison key, or an empty string.</returns>
    /// <remarks>
    /// Folds case and diacritics, drops punctuation and a trailing year in brackets, and collapses
    /// whitespace. It deliberately does not strip articles or subtitles: "The Office" and "Office" are
    /// genuinely different shows, and over-normalizing produces confident wrong matches.
    /// </remarks>
    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var withoutYear = TrailingYearRegex().Replace(title, string.Empty);
        var decomposed = withoutYear.Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or ':' or '.' or '\'' or '"')
            {
                builder.Append(' ');
            }
        }

        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    /// <summary>
    /// Computes a 0..1 similarity between two already-normalized titles.
    /// </summary>
    /// <param name="left">First normalized title.</param>
    /// <param name="right">Second normalized title.</param>
    /// <returns>1 for identical strings, 0 for entirely dissimilar ones.</returns>
    public static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 1;
        }

        var distance = LevenshteinDistance(left, right);
        var longest = Math.Max(left.Length, right.Length);
        return 1.0 - ((double)distance / longest);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        // Two-row variant: titles are short, and this keeps allocation proportional to the shorter one.
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\s*[\(\[]\s*(19|20)\d{2}\s*[\)\]]\s*$", RegexOptions.Compiled)]
    private static partial Regex TrailingYearRegex();
}
