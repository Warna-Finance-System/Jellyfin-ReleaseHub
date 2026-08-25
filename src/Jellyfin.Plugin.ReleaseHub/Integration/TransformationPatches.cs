using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ReleaseHub.Integration;

/// <summary>
/// Transformation callbacks invoked by the File Transformation plugin.
/// </summary>
/// <remarks>
/// These methods are reached only through reflection, by fully-qualified name, from the payload
/// registered in <see cref="FileTransformationHook"/>. Renaming this class or its methods without
/// updating that payload silently disables the user-facing entry point.
/// </remarks>
public static partial class TransformationPatches
{
    /// <summary>
    /// Marker comment used to detect an injection that is already present.
    /// </summary>
    private const string Marker = "<!-- ReleaseHub -->";

    /// <summary>
    /// Gets a value indicating whether an <c>index.html</c> has been transformed since the last start.
    /// </summary>
    /// <remarks>
    /// Everything else reported here is only meaningful once the document has actually been seen.
    /// Without this flag the settings page could not tell "Abyss is not installed" apart from "nobody
    /// has loaded the web interface yet", and would state the first while meaning the second.
    /// </remarks>
    public static bool IndexHtmlObserved { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the last observed document carried the Abyss spotlight loader.
    /// </summary>
    public static bool AbyssSpotlightDetected { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the Abyss spotlight loader was stripped from the last document.
    /// </summary>
    public static bool AbyssSpotlightRemoved { get; private set; }

    /// <summary>
    /// Transforms jellyfin-web's <c>index.html</c> on its way to the browser.
    /// </summary>
    /// <param name="payload">The current file contents supplied by File Transformation.</param>
    /// <returns>The transformed contents, or the input unchanged if it could not be patched.</returns>
    public static string IndexHtml(PatchRequestPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var contents = payload.Contents;
        if (string.IsNullOrEmpty(contents))
        {
            return string.Empty;
        }

        contents = ApplyAbyssSpotlight(contents);
        return InjectBootScript(contents);
    }

    /// <summary>
    /// Records whether the Abyss spotlight is present, and removes it when it has been switched off.
    /// </summary>
    /// <param name="contents">The document being served.</param>
    /// <returns>The document, with the spotlight loader stripped when hiding is enabled.</returns>
    /// <remarks>
    /// Detection runs whether or not hiding is enabled: an administrator deciding whether to turn this
    /// on needs to know that there is something to turn off, and after turning it on needs to see that
    /// it took effect. Reporting only when acting would leave both questions unanswered.
    /// </remarks>
    private static string ApplyAbyssSpotlight(string contents)
    {
        var detected = HasAbyssSpotlight(contents);

        IndexHtmlObserved = true;
        AbyssSpotlightDetected = detected;

        if (!detected || !Plugin.Config.HideAbyssSpotlight)
        {
            AbyssSpotlightRemoved = false;
            return contents;
        }

        AbyssSpotlightRemoved = true;

        // Removed from the response only. The file on disk, and every asset Abyss installed beside it,
        // are left exactly as they were, so this is undone by clearing the setting rather than by
        // reinstalling the theme.
        return StripAbyssSpotlight(contents);
    }

    /// <summary>
    /// Reports whether a document carries the Abyss spotlight loader.
    /// </summary>
    /// <param name="contents">The document to inspect.</param>
    /// <returns><see langword="true"/> when the loader element is present.</returns>
    /// <remarks>
    /// Kept free of any configuration lookup so that recognition can be exercised directly. What
    /// counts as the loader is the part most likely to drift as Abyss changes, and it is worth
    /// nothing if the tests verify a copy of the expression rather than the expression itself.
    /// </remarks>
    internal static bool HasAbyssSpotlight(string contents) => AbyssSpotlightTag().IsMatch(contents);

    /// <summary>
    /// Returns the document with every Abyss spotlight loader element removed.
    /// </summary>
    /// <param name="contents">The document to strip.</param>
    /// <returns>The document without the loader.</returns>
    internal static string StripAbyssSpotlight(string contents) =>
        AbyssSpotlightTag().Replace(contents, string.Empty);

    /// <summary>
    /// Injects the ReleaseHub bootstrap script before the closing body tag.
    /// </summary>
    /// <param name="contents">The document being served.</param>
    /// <returns>The document with the bootstrap script, or unchanged when it cannot be placed.</returns>
    private static string InjectBootScript(string contents)
    {
        if (contents.Contains(Marker, StringComparison.Ordinal))
        {
            return contents;
        }

        var closingBody = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (closingBody < 0)
        {
            // Not the document we expected. Returning the input untouched is always safe: the worst
            // outcome is that ReleaseHub stays reachable only from the dashboard.
            return contents;
        }

        // The script is served by Jellyfin's own plugin page endpoint, which requires no authentication,
        // so it loads for signed-out visitors too and simply does nothing until a user is signed in.
        var injection = string.Format(
            CultureInfo.InvariantCulture,
            "{0}<script defer src=\"{1}\"></script>{0}",
            Marker,
            FileTransformationHook.BootScriptPath);

        return contents[..closingBody] + injection + contents[closingBody..];
    }

    /// <summary>
    /// Matches the empty script element Abyss writes into <c>index.html</c> to mount its spotlight.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    /// <remarks>
    /// Either signal is enough to identify it — the <c>data-abyss-spotlight</c> attribute or the
    /// <c>spotlight-loader.js</c> source — so a future Abyss that renames one of them, or reorders its
    /// attributes, is still recognised. Only an element with nothing between its tags is matched, so
    /// an inline script that merely mentions the name in its own code is never swallowed.
    /// </remarks>
    [GeneratedRegex(
        """<script\b[^>]*(?:data-abyss-spotlight|spotlight-loader\.js)[^>]*>\s*</script>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex AbyssSpotlightTag();
}
