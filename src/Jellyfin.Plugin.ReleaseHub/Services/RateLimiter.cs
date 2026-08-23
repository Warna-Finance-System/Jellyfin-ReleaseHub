using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ReleaseHub.Services;

/// <summary>
/// A sliding-window rate limiter with support for provider-imposed cool-offs.
/// </summary>
/// <remarks>
/// <para>
/// One instance exists per provider, because each provider publishes its own budget. The window is
/// deliberately configured below what a provider advertises: TVMaze documents "at least 20 calls every
/// 10 seconds" and ReleaseHub stays under that rather than probing for the real ceiling.
/// </para>
/// <para>
/// Waiting is serialized behind a single gate. That makes the accounting exact — the alternative,
/// computing a delay and then releasing the lock before sleeping, lets a burst slip through — and costs
/// nothing here, since the point of the class is to prevent concurrent bursts in the first place.
/// </para>
/// </remarks>
public sealed class RateLimiter : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTimeOffset> _recent = new();
    private readonly int _permits;
    private readonly TimeSpan _window;

    private DateTimeOffset _notBefore = DateTimeOffset.MinValue;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimiter"/> class.
    /// </summary>
    /// <param name="permits">How many requests are allowed inside <paramref name="window"/>.</param>
    /// <param name="window">The length of the sliding window.</param>
    public RateLimiter(int permits, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permits, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        _permits = permits;
        _window = window;
    }

    /// <summary>
    /// Gets the current cool-off expiry, or <see cref="DateTimeOffset.MinValue"/> when not cooling off.
    /// </summary>
    public DateTimeOffset CoolOffUntil => _notBefore;

    /// <summary>
    /// Waits until another request may be issued, then records it.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the caller may proceed.</returns>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                var now = DateTimeOffset.UtcNow;

                // A 429 with Retry-After outranks the local window: the provider has told us exactly
                // how long to stay away, and guessing shorter would be a deliberate limit bypass.
                if (now < _notBefore)
                {
                    await Task.Delay(_notBefore - now, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                while (_recent.Count > 0 && now - _recent.Peek() >= _window)
                {
                    _recent.Dequeue();
                }

                if (_recent.Count < _permits)
                {
                    _recent.Enqueue(now);
                    return;
                }

                var waitFor = _window - (now - _recent.Peek());
                if (waitFor > TimeSpan.Zero)
                {
                    await Task.Delay(waitFor, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Records a provider-imposed cool-off, typically from a <c>429</c> response's <c>Retry-After</c>.
    /// </summary>
    /// <param name="duration">How long to stay away.</param>
    /// <remarks>
    /// Only ever extends the cool-off. A later response asking for a shorter wait must not shorten a
    /// longer one already in effect.
    /// </remarks>
    public void ApplyCoolOff(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var until = DateTimeOffset.UtcNow + duration;
        if (until > _notBefore)
        {
            _notBefore = until;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}
