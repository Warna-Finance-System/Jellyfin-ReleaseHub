using Jellyfin.Plugin.ReleaseHub.Integration;
using Jellyfin.Plugin.ReleaseHub.Providers;
using Jellyfin.Plugin.ReleaseHub.Providers.AnimeSchedule;
using Jellyfin.Plugin.ReleaseHub.Providers.Tmdb;
using Jellyfin.Plugin.ReleaseHub.Providers.TvMaze;
using Jellyfin.Plugin.ReleaseHub.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.ReleaseHub;

/// <summary>
/// Registers ReleaseHub's services into the Jellyfin host's dependency injection container.
/// </summary>
/// <remarks>
/// Jellyfin discovers this type by scanning the plugin assembly for <see cref="IPluginServiceRegistrator"/>
/// implementations at startup, before the host is built.
/// </remarks>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Singletons: the cache owns a database handle and the providers each own a rate limiter whose
        // window has to be shared across every caller, so a transient registration would defeat both.
        serviceCollection.AddSingleton<CacheService>();
        serviceCollection.AddSingleton<LibraryDiscoveryService>();

        serviceCollection.AddSingleton<TvMazeProvider>();
        serviceCollection.AddSingleton<AnimeScheduleProvider>();
        serviceCollection.AddSingleton<TmdbProvider>();

        // Registered through the interface as well so ReleaseService and the controller receive every
        // provider without naming them; adding a third provider means adding one line here.
        serviceCollection.AddSingleton<IReleaseProvider>(sp => sp.GetRequiredService<TvMazeProvider>());
        serviceCollection.AddSingleton<IReleaseProvider>(sp => sp.GetRequiredService<AnimeScheduleProvider>());
        serviceCollection.AddSingleton<IReleaseProvider>(sp => sp.GetRequiredService<TmdbProvider>());

        serviceCollection.AddSingleton<ReleaseService>();

        // Optional web integration. Registered unconditionally: the hook itself detects whether the
        // File Transformation plugin is present and does nothing when it is not.
        serviceCollection.AddHostedService<FileTransformationHook>();
    }
}
