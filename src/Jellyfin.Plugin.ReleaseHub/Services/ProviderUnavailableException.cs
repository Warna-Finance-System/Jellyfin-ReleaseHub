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

    /// <summary>
    /// Gets or sets a value indicating whether the provider rejected the credential.
    /// </summary>
    /// <remarks>
    /// Distinguished from a transient outage because the two deserve opposite handling: a timeout is
    /// worth retrying on the next series, whereas a rejected key will fail identically every time.
    /// Repeating it once per series would spend the request budget on certain failures and hammer an
    /// endpoint that has already said no.
    /// </remarks>
    public bool IsAuthenticationFailure { get; set; }
}
