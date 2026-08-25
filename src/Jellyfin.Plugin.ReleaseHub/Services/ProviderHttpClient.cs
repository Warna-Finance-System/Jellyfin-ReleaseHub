using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ReleaseHub.Services;

/// <summary>
/// HTTP access to one external provider, with rate limiting, retries and request de-duplication.
/// </summary>
/// <remarks>
/// Every outbound provider call in ReleaseHub goes through this type, so the politeness rules live in
/// exactly one place: a provider implementation cannot accidentally bypass the limiter by reaching for
/// an <see cref="HttpClient"/> of its own.
/// </remarks>
public sealed class ProviderHttpClient : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly RateLimiter _rateLimiter;
    private readonly string _providerName;
    private readonly int _maxAttempts;

    /// <summary>
    /// Requests currently being awaited, keyed by absolute URL, so that concurrent callers asking for
    /// the same resource share one round trip instead of racing each other into the rate limiter.
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inFlight = new(StringComparer.Ordinal);

    private int _requestCount;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderHttpClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin's HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="providerName">Provider name, used only for log messages.</param>
    /// <param name="permits">Requests allowed per <paramref name="window"/>.</param>
    /// <param name="window">Length of the rate-limiting window.</param>
    /// <param name="maxAttempts">How many times a single request may be attempted.</param>
    public ProviderHttpClient(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        string providerName,
        int permits,
        TimeSpan window,
        int maxAttempts = 4)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _providerName = providerName;
        _rateLimiter = new RateLimiter(permits, window);
        _maxAttempts = maxAttempts;
    }

    /// <summary>
    /// Gets the number of requests actually sent since this client was created.
    /// </summary>
    /// <remarks>Used to enforce the configured per-synchronization request ceiling.</remarks>
    public int RequestCount => Volatile.Read(ref _requestCount);

    /// <summary>
    /// Fetches a URL and deserializes the JSON body.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="url">The absolute URL to fetch.</param>
    /// <param name="configureRequest">
    /// Optional hook to add headers, used for bearer authentication. Runs on every attempt.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The deserialized body, or <see langword="null"/> when the resource does not exist.</returns>
    /// <exception cref="ProviderUnavailableException">
    /// The provider could not be reached, or kept failing after every attempt.
    /// </exception>
    public async Task<T?> GetJsonAsync<T>(
        Uri url,
        Action<HttpRequestMessage>? configureRequest,
        CancellationToken cancellationToken)
        where T : class
    {
        var body = await GetStringAsync(url, configureRequest, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderUnavailableException(
                _providerName,
                $"{_providerName} returned a response that could not be parsed.",
                ex);
        }
    }

    /// <summary>
    /// Fetches a URL and returns the raw body.
    /// </summary>
    /// <param name="url">The absolute URL to fetch.</param>
    /// <param name="configureRequest">Optional hook to add headers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The body, or <see langword="null"/> for a 404.</returns>
    public Task<string?> GetStringAsync(
        Uri url,
        Action<HttpRequestMessage>? configureRequest,
        CancellationToken cancellationToken)
    {
        var key = url.AbsoluteUri;

        // Lazy with ExecutionAndPublication guarantees the factory runs once even under contention,
        // so N concurrent callers produce exactly one HTTP request.
        var lazy = _inFlight.GetOrAdd(
            key,
            _ => new Lazy<Task<string?>>(
                () => SendWithRetriesAsync(url, configureRequest, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return AwaitAndEvictAsync(key, lazy);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _rateLimiter.Dispose();
    }

    private async Task<string?> AwaitAndEvictAsync(string key, Lazy<Task<string?>> lazy)
    {
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            // Evicted as soon as it settles: this de-duplicates concurrent requests, it is not a
            // response cache. Caching with expiry is CacheService's job.
            _inFlight.TryRemove(key, out _);
        }
    }

    private async Task<string?> SendWithRetriesAsync(
        Uri url,
        Action<HttpRequestMessage>? configureRequest,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                configureRequest?.Invoke(request);

                var client = _httpClientFactory.CreateClient(NamedClient.Default);
                Interlocked.Increment(ref _requestCount);

                using var response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = GetRetryAfter(response) ?? BackoffFor(attempt);
                    _rateLimiter.ApplyCoolOff(retryAfter);

                    _logger.LogWarning(
                        "{Provider} rate limited this request; honouring a {Seconds:0.#}s cool-off (attempt {Attempt}/{Max})",
                        _providerName,
                        retryAfter.TotalSeconds,
                        attempt,
                        _maxAttempts);

                    lastError = new ProviderUnavailableException(_providerName, $"{_providerName} is rate limiting requests.");
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    // Retrying will not fix a bad credential, and hammering an auth endpoint is rude.
                    throw new ProviderUnavailableException(
                        _providerName,
                        $"{_providerName} rejected the request ({(int)response.StatusCode}). Check the API key.")
                    {
                        IsAuthenticationFailure = true
                    };
                }

                if ((int)response.StatusCode >= 500)
                {
                    lastError = new ProviderUnavailableException(
                        _providerName,
                        $"{_providerName} returned {(int)response.StatusCode}.");

                    await DelayBackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                ApplyAdvertisedBudget(response);

                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ProviderUnavailableException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
                _logger.LogDebug(
                    ex,
                    "{Provider} request failed on attempt {Attempt}/{Max}",
                    _providerName,
                    attempt,
                    _maxAttempts);

                if (attempt < _maxAttempts)
                {
                    await DelayBackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw new ProviderUnavailableException(
            _providerName,
            $"{_providerName} did not respond successfully after {_maxAttempts} attempts.",
            lastError);
    }

    /// <summary>
    /// Honours a provider's advertised remaining budget before it has to say no.
    /// </summary>
    /// <param name="response">A successful response.</param>
    /// <remarks>
    /// AnimeSchedule reports <c>x-ratelimit-remaining</c> and <c>x-ratelimit-reset</c> on every
    /// response. Pausing when the budget is spent means ReleaseHub stops on its own rather than being
    /// stopped by a 429 — which is what respecting a rate limit actually means. Providers that send no
    /// such headers, like TVMaze, are unaffected and stay governed by the local window.
    /// </remarks>
    private void ApplyAdvertisedBudget(HttpResponseMessage response)
    {
        if (!TryGetHeaderValue(response, "x-ratelimit-remaining", out var remaining) || remaining > 0)
        {
            return;
        }

        if (!TryGetHeaderValue(response, "x-ratelimit-reset", out var resetUnixSeconds))
        {
            return;
        }

        var resetAt = DateTimeOffset.FromUnixTimeSeconds(resetUnixSeconds);
        var wait = resetAt - DateTimeOffset.UtcNow;
        if (wait <= TimeSpan.Zero)
        {
            return;
        }

        _rateLimiter.ApplyCoolOff(wait);
        _logger.LogInformation(
            "{Provider} budget exhausted; pausing {Seconds:0.#}s until it resets",
            _providerName,
            wait.TotalSeconds);
    }

    private static bool TryGetHeaderValue(HttpResponseMessage response, string name, out long value)
    {
        value = 0;

        if (!response.Headers.TryGetValues(name, out var values))
        {
            return false;
        }

        foreach (var candidate in values)
        {
            if (long.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    private static TimeSpan BackoffFor(int attempt)
    {
        // Exponential with jitter, so several series resolving at once do not retry in lockstep.
        var seconds = Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * 0.5;
        return TimeSpan.FromSeconds(Math.Min(seconds + jitter, 30));
    }

    private static Task DelayBackoffAsync(int attempt, CancellationToken cancellationToken)
        => Task.Delay(BackoffFor(attempt), cancellationToken);

    /// <summary>
    /// Formats a URL for logging.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <returns>The URL without its query string.</returns>
    /// <remarks>
    /// Query strings are dropped because a future provider might carry a token there. ReleaseHub's own
    /// providers authenticate with headers, but the log must stay safe regardless.
    /// </remarks>
    internal static string Redact(Uri url)
        => string.Create(CultureInfo.InvariantCulture, $"{url.Scheme}://{url.Host}{url.AbsolutePath}");
}
