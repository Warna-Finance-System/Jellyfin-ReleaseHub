using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ReleaseHub.Models;

namespace Jellyfin.Plugin.ReleaseHub.Providers;

/// <summary>
/// A source of release information.
/// </summary>
/// <remarks>
/// Implementations own everything provider-specific — endpoints, authentication, rate limits, DTO
/// shapes — and hand back only the normalized types in <c>Models</c>. Adding a third provider must not
/// require touching the services or the frontend.
/// </remarks>
public interface IReleaseProvider
{
    /// <summary>
    /// Gets the provider this implementation represents.
    /// </summary>
    ReleaseProviderKind Kind { get; }

    /// <summary>
    /// Gets a value indicating whether the provider is enabled and has everything it needs to run.
    /// </summary>
    /// <remarks>
    /// A provider that is switched off, or is missing a required credential, reports <see langword="false"/>
    /// and is skipped entirely rather than being called and failing. This is what lets the anime half of
    /// ReleaseHub disable itself cleanly when no AnimeSchedule key is configured.
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>
    /// Searches the provider for series matching free text.
    /// </summary>
    /// <param name="query">The user's search text.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Matching series, best first, or an empty list.</returns>
    Task<IReadOnlyList<ProviderSeries>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches a single series by this provider's own identifier.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The series, or <see langword="null"/> when the provider does not know it.</returns>
    Task<ProviderSeries?> GetSeriesAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to find the provider's record for a series Jellyfin already knows about.
    /// </summary>
    /// <param name="identity">What Jellyfin knows: titles, year and existing provider identifiers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The best candidate together with how much it can be trusted.</returns>
    /// <remarks>
    /// Implementations must prefer identifier lookups over title matching, and must report a low
    /// confidence rather than a confident wrong answer. The caller decides what to do with a weak match.
    /// </remarks>
    Task<ProviderMatch?> ResolveAsync(SeriesIdentity identity, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a value indicating whether the provider can return a whole schedule window in one call.
    /// </summary>
    /// <remarks>
    /// The two providers have opposite cost models and the caller has to know which it is dealing with.
    /// AnimeSchedule returns an entire week per request, so fetching the window once and filtering is far
    /// cheaper than asking per series. TVMaze's equivalent endpoint returns several megabytes covering
    /// every show it knows, so there the per-series path wins.
    /// </remarks>
    bool SupportsBulkSchedule { get; }

    /// <summary>
    /// Gets how far ahead this provider is worth asking, or <see langword="null"/> when the window
    /// costs it nothing.
    /// </summary>
    /// <remarks>
    /// The synchronization horizon is chosen for what users want to see, not for what each provider
    /// charges to see it. A provider whose cost is independent of the window — one request per series,
    /// or an episode list returned whole — reports <see langword="null"/> and is asked for the entire
    /// horizon. A provider billed per week of window reports the point past which the extra requests
    /// buy nothing, so that widening the horizon for everyone else does not quietly multiply its
    /// traffic against weeks it has no data for yet.
    /// </remarks>
    TimeSpan? MaxLookAhead { get; }

    /// <summary>
    /// Fetches every release the provider knows about inside a window.
    /// </summary>
    /// <param name="fromUtc">Inclusive start of the window.</param>
    /// <param name="toUtc">Inclusive end of the window.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// Normalized releases, or an empty list when <see cref="SupportsBulkSchedule"/> is
    /// <see langword="false"/>.
    /// </returns>
    Task<IReadOnlyList<ReleaseItem>> GetScheduleAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the releases known for a series inside a window.
    /// </summary>
    /// <param name="series">A series previously returned by this provider.</param>
    /// <param name="fromUtc">Inclusive start of the window.</param>
    /// <param name="toUtc">Inclusive end of the window.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Normalized releases, which may be empty.</returns>
    Task<IReadOnlyList<ReleaseItem>> GetReleasesAsync(
        ProviderSeries series,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies that the provider is reachable and, where applicable, that the credential works.
    /// </summary>
    /// <param name="credentialOverride">
    /// A credential to test instead of the stored one, or <see langword="null"/> to use what is saved.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome, suitable for display in the settings page.</returns>
    /// <remarks>
    /// The override exists so an administrator can check a key before committing it: testing only the
    /// saved value forces them to save a possibly wrong key first, which is how a working configuration
    /// gets overwritten by a typo. Providers without a credential ignore it.
    /// </remarks>
    Task<ProviderTestResult> TestConnectionAsync(
        string? credentialOverride,
        CancellationToken cancellationToken);
}
