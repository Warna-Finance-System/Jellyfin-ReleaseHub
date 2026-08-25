namespace Jellyfin.Plugin.ReleaseHub.Api.Models;

/// <summary>
/// What ReleaseHub currently observes about the web interface it is served alongside.
/// </summary>
/// <remarks>
/// Reported rather than assumed. Every field here is the outcome of the last real transformation of
/// <c>index.html</c>, so the settings page states what is happening instead of what ought to happen —
/// which is the whole difficulty with an injected interface: the same symptom can mean the theme is
/// absent, the integration is missing, or the page simply has not been loaded yet.
/// </remarks>
public sealed class InterfaceStatusDto
{
    /// <summary>
    /// Gets or sets a value indicating whether the File Transformation plugin accepted ReleaseHub's
    /// registration.
    /// </summary>
    /// <remarks>
    /// Nothing on this page can work without it: it is the only mechanism by which a plugin can alter
    /// the document jellyfin-web serves.
    /// </remarks>
    public bool FileTransformationAvailable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an <c>index.html</c> has been transformed since the
    /// server started.
    /// </summary>
    public bool IndexHtmlObserved { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Abyss spotlight loader was found in that document.
    /// </summary>
    public bool AbyssSpotlightDetected { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether it was stripped from that document.
    /// </summary>
    public bool AbyssSpotlightRemoved { get; set; }

    /// <summary>
    /// Gets or sets the configured preference, which may not yet have taken effect.
    /// </summary>
    public bool HideAbyssSpotlight { get; set; }
}
