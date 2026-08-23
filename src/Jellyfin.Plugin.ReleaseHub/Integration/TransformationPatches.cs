using System;
using System.Globalization;

namespace Jellyfin.Plugin.ReleaseHub.Integration;

/// <summary>
/// Transformation callbacks invoked by the File Transformation plugin.
/// </summary>
/// <remarks>
/// These methods are reached only through reflection, by fully-qualified name, from the payload
/// registered in <see cref="FileTransformationHook"/>. Renaming this class or its methods without
/// updating that payload silently disables the user-facing entry point.
/// </remarks>
public static class TransformationPatches
{
    /// <summary>
    /// Marker comment used to detect an injection that is already present.
    /// </summary>
    private const string Marker = "<!-- ReleaseHub -->";

    /// <summary>
    /// Injects the ReleaseHub bootstrap script into jellyfin-web's <c>index.html</c>.
    /// </summary>
    /// <param name="payload">The current file contents supplied by File Transformation.</param>
    /// <returns>The transformed contents, or the input unchanged if it could not be patched.</returns>
    public static string IndexHtml(PatchRequestPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var contents = payload.Contents;
        if (string.IsNullOrEmpty(contents) || contents.Contains(Marker, StringComparison.Ordinal))
        {
            return contents ?? string.Empty;
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
}
