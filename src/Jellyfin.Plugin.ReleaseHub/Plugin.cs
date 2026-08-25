using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.ReleaseHub.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ReleaseHub;

/// <summary>
/// The ReleaseHub plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The stable plugin identifier. Changing it would orphan every existing installation's configuration.
    /// </summary>
    public const string PluginGuid = "8d9233a8-2ce9-4603-b5e2-e26f89f9408c";

    /// <summary>
    /// Page name of the administrator configuration page.
    /// </summary>
    public const string ConfigPageName = "ReleaseHub";

    /// <summary>
    /// Page name of the administrator page covering ReleaseHub's web-interface adjustments.
    /// </summary>
    public const string InterfacePageName = "ReleaseHubInterface";

    /// <summary>
    /// Page name of the ReleaseHub application shell.
    /// </summary>
    public const string AppPageName = "releasehub-app";

    /// <summary>
    /// Page name of the bootstrap script injected into <c>index.html</c>.
    /// </summary>
    public const string BootScriptName = "releasehub-boot.js";

    private const string ResourcePrefix = "Jellyfin.Plugin.ReleaseHub.";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    /// <remarks>
    /// Jellyfin constructs exactly one plugin instance per server. Services resolved from DI read the
    /// live configuration through this property so that a configuration save takes effect immediately.
    /// </remarks>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Jellyfin ReleaseHub";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(PluginGuid);

    /// <inheritdoc />
    public override string Description =>
        "Unified TV series, anime and film release calendar, tracking and discovery for Jellyfin.";

    /// <summary>
    /// Gets the plugin configuration, falling back to defaults before the plugin has been constructed.
    /// </summary>
    public static PluginConfiguration Config => Instance?.Configuration ?? new PluginConfiguration();

    /// <inheritdoc />
    /// <remarks>
    /// Everything registered here becomes reachable at <c>GET /web/ConfigurationPage?name={Name}</c>.
    /// That endpoint carries no authorization attribute, so these assets can be loaded by an ordinary
    /// <c>&lt;script&gt;</c> or <c>fetch</c> from any signed-in or signed-out browser. Only non-secret
    /// static assets belong here; user data is served by the authenticated ReleaseHub API instead.
    ///
    /// The page marked <see cref="PluginPageInfo.EnableInMainMenu"/> is also the page the dashboard's
    /// plugin list links to, so it is the configuration page rather than the application shell.
    /// </remarks>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = ConfigPageName,
            DisplayName = "ReleaseHub",
            EmbeddedResourcePath = ResourcePrefix + "Configuration.configPage.html",
            EnableInMainMenu = true,
            MenuSection = "ReleaseHub",
            MenuIcon = "event"
        };

        // Reached from a link on the configuration page rather than from the dashboard menu: it is a
        // continuation of those settings, not a second plugin, and a second menu entry named after the
        // same plugin reads as one.
        yield return new PluginPageInfo
        {
            Name = InterfacePageName,
            DisplayName = "ReleaseHub — Web interface",
            EmbeddedResourcePath = ResourcePrefix + "Configuration.interfacePage.html"
        };

        yield return new PluginPageInfo
        {
            Name = AppPageName,
            EmbeddedResourcePath = ResourcePrefix + "Web.app.html"
        };

        yield return new PluginPageInfo
        {
            Name = BootScriptName,
            EmbeddedResourcePath = ResourcePrefix + "Web.boot.js"
        };

        yield return new PluginPageInfo
        {
            Name = "releasehub-app.js",
            EmbeddedResourcePath = ResourcePrefix + "Web.app.js"
        };

        yield return new PluginPageInfo
        {
            Name = "releasehub.css",
            EmbeddedResourcePath = ResourcePrefix + "Web.releasehub.css"
        };

        foreach (var culture in LocalizationCultures)
        {
            yield return new PluginPageInfo
            {
                Name = string.Format(CultureInfo.InvariantCulture, "releasehub-{0}.json", culture),
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}Localization.{1}.json",
                    ResourcePrefix,
                    culture)
            };
        }
    }

    /// <summary>
    /// Gets the cultures shipped with the plugin, in the order they are offered in the settings page.
    /// </summary>
    public static IReadOnlyList<string> LocalizationCultures { get; } = new[] { "en-US", "fr-FR" };
}
