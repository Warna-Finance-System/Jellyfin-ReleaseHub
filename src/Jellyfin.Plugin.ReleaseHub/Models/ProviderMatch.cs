namespace Jellyfin.Plugin.ReleaseHub.Models;

/// <summary>
/// The result of trying to link a Jellyfin series to a provider record.
/// </summary>
/// <param name="Series">The candidate the provider returned.</param>
/// <param name="Confidence">How much the candidate can be trusted.</param>
/// <param name="Reason">A short, loggable explanation of how the match was made.</param>
public sealed record ProviderMatch(ProviderSeries Series, MatchConfidence Confidence, string Reason)
{
    /// <summary>
    /// Gets a value indicating whether this match is strong enough to store without asking the user.
    /// </summary>
    /// <remarks>
    /// Deliberately strict. A wrong automatic association silently fills someone's calendar with the
    /// wrong show and is far more annoying than a series that simply shows nothing until it is matched
    /// by hand, so anything short of a strong match is treated as a suggestion.
    /// </remarks>
    public bool IsAutoAcceptable => Confidence >= MatchConfidence.High;
}

/// <summary>
/// The outcome of a provider connectivity check.
/// </summary>
/// <param name="Success">Whether the provider answered successfully.</param>
/// <param name="Message">A short, user-facing explanation. Never contains a credential.</param>
public sealed record ProviderTestResult(bool Success, string Message)
{
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="message">The message to show.</param>
    /// <returns>A successful <see cref="ProviderTestResult"/>.</returns>
    public static ProviderTestResult Ok(string message = "Connection succeeded.")
        => new(true, message);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="message">The message to show.</param>
    /// <returns>A failed <see cref="ProviderTestResult"/>.</returns>
    public static ProviderTestResult Fail(string message) => new(false, message);
}
