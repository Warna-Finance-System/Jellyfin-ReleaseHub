namespace Jellyfin.Plugin.ReleaseHub.Integration;

/// <summary>
/// The argument the File Transformation plugin passes to a registered transformation callback.
/// </summary>
/// <remarks>
/// File Transformation invokes the callback through reflection and deserializes its JSON request into
/// whatever type the callback declares, so this type is deliberately defined locally instead of being
/// referenced from that plugin. That keeps ReleaseHub free of any compile-time dependency on it, which
/// is what allows the plugin to load and run normally when File Transformation is not installed.
/// </remarks>
public sealed class PatchRequestPayload
{
    /// <summary>
    /// Gets or sets the current contents of the file being served, after any earlier transformation.
    /// </summary>
    public string? Contents { get; set; }
}
