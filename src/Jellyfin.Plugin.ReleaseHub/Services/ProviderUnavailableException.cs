using System;

namespace Jellyfin.Plugin.ReleaseHub.Services;

/// <summary>
/// Thrown when an external provider cannot be reached or refuses to answer.
/// </summary>
/// <remarks>
/// Callers are expected to catch this per provider and fall back to cached data. One provider being
/// down must never take the other one — or the UI — with it.
/// </remarks>
public sealed class ProviderUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderUnavailableException"/> class.
    /// </summary>
    /// <param name="providerName">The provider that failed.</param>
    /// <param name="message">A message safe to show to a user.</param>
    public ProviderUnavailableException(string providerName, string message)
        : base(message)
    {
        ProviderName = providerName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderUnavailableException"/> class.
    /// </summary>
    /// <param name="providerName">The provider that failed.</param>
    /// <param name="message">A message safe to show to a user.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ProviderUnavailableException(string providerName, string message, Exception? innerException)
        : base(message, innerException)
    {
        ProviderName = providerName;
    }

    /// <summary>
    /// Gets the provider that failed.
    /// </summary>
    public string ProviderName { get; }
}
