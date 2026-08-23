using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.ReleaseHub.Integration;

/// <summary>
/// Registers ReleaseHub's <c>index.html</c> transformation with the File Transformation plugin.
/// </summary>
/// <remarks>
/// <para>
/// Jellyfin 10.11 has no supported way for a plugin to add a page that non-administrator users can
/// reach: the SPA route behind <c>#/configurationpage</c> is wrapped in an admin route guard. The
/// community's File Transformation plugin is the only mechanism available for adding an entry point
/// that every user sees, so ReleaseHub uses it when it happens to be installed.
/// </para>
/// <para>
/// The integration is entirely reflective and entirely optional. ReleaseHub carries no assembly
/// reference to File Transformation, tolerates every failure mode silently, and remains fully usable
/// from <c>Dashboard &gt; ReleaseHub</c> when the hook does not fire.
/// </para>
/// </remarks>
public sealed class FileTransformationHook : IHostedService
{
    /// <summary>
    /// Stable identifier for this transformation. File Transformation keys its registry on this value,
    /// so it must not change between releases or a server restart would accumulate duplicates.
    /// </summary>
    private const string TransformationId = "3f2c0b41-6d7e-4f9a-9c15-4a8b2d6e7f30";

    private const string FileTransformationAssemblyMarker = ".FileTransformation";
    private const string PluginInterfaceTypeName = "Jellyfin.Plugin.FileTransformation.PluginInterface";
    private const string RegisterMethodName = "RegisterTransformation";

    private readonly ILogger<FileTransformationHook> _logger;
    private readonly IServerConfigurationManager _serverConfigurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTransformationHook"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="serverConfigurationManager">Used to honour a configured base URL prefix.</param>
    public FileTransformationHook(
        ILogger<FileTransformationHook> logger,
        IServerConfigurationManager serverConfigurationManager)
    {
        _logger = logger;
        _serverConfigurationManager = serverConfigurationManager;
    }

    /// <summary>
    /// Gets a value indicating whether the transformation was accepted by File Transformation.
    /// </summary>
    /// <remarks>
    /// Surfaced through the ReleaseHub status endpoint so the settings page can tell the administrator
    /// whether ordinary users currently get a ReleaseHub entry in the main menu.
    /// </remarks>
    public static bool IsUserTabAvailable { get; private set; }

    /// <summary>
    /// Gets the absolute path of the bootstrap script, including any configured base URL prefix.
    /// </summary>
    public static string BootScriptPath { get; private set; } =
        "/web/ConfigurationPage?name=" + Plugin.BootScriptName;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        BootScriptPath = BuildBootScriptPath();

        try
        {
            Register();
        }
        catch (Exception ex)
        {
            // A broken optional integration must never prevent the plugin — or the server — from starting.
            IsUserTabAvailable = false;
            _logger.LogWarning(
                ex,
                "ReleaseHub could not register its web transformation. ReleaseHub remains available from the dashboard");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Register()
    {
        var assembly = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(candidate =>
                candidate.FullName?.Contains(FileTransformationAssemblyMarker, StringComparison.Ordinal) == true);

        if (assembly is null)
        {
            IsUserTabAvailable = false;
            _logger.LogInformation(
                "File Transformation plugin not found. ReleaseHub is reachable from Dashboard > ReleaseHub; "
                + "install File Transformation to also show ReleaseHub in the main menu for all users");
            return;
        }

        var pluginInterface = assembly.GetType(PluginInterfaceTypeName);
        var register = pluginInterface?.GetMethod(RegisterMethodName, BindingFlags.Public | BindingFlags.Static);

        if (register is null)
        {
            IsUserTabAvailable = false;
            _logger.LogWarning(
                "Found {Assembly} but its {Type}.{Method} entry point is missing. "
                + "The File Transformation plugin may be a version ReleaseHub does not understand",
                assembly.GetName().Name,
                PluginInterfaceTypeName,
                RegisterMethodName);
            return;
        }

        var payload = new JObject
        {
            ["id"] = TransformationId,
            ["fileNamePattern"] = "index.html",
            ["callbackAssembly"] = typeof(TransformationPatches).Assembly.FullName,
            ["callbackClass"] = typeof(TransformationPatches).FullName,
            ["callbackMethod"] = nameof(TransformationPatches.IndexHtml)
        };

        register.Invoke(null, new object?[] { payload });

        IsUserTabAvailable = true;
        _logger.LogInformation("ReleaseHub registered its web transformation with File Transformation");
    }

    private string BuildBootScriptPath()
    {
        // A server published under a path prefix (network.xml BaseUrl) serves jellyfin-web from
        // {prefix}/web/, so the injected script tag has to carry the same prefix.
        var baseUrl = _serverConfigurationManager.GetNetworkConfiguration().BaseUrl ?? string.Empty;
        baseUrl = baseUrl.TrimEnd('/');

        if (baseUrl.Length > 0 && !baseUrl.StartsWith('/'))
        {
            baseUrl = "/" + baseUrl;
        }

        return baseUrl + "/web/ConfigurationPage?name=" + Plugin.BootScriptName;
    }
}
