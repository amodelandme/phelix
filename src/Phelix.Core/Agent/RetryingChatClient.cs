using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.AI;
using Phelix.Core.Config;

namespace Phelix.Core.Agent;

/// <summary>
/// <see cref="IChatClient"/> middleware that retries transient failures with exponential backoff and jitter.
/// </summary>
/// <remarks>
/// Wraps an inner <see cref="IChatClient"/> and intercepts <see cref="HttpRequestException"/> (429 / 5xx),
/// <see cref="TaskCanceledException"/>, <see cref="TimeoutException"/>, and <see cref="IOException"/>.
/// Non-transient errors are rethrown immediately without consuming any retry budget.
///
/// For streaming responses, the middleware buffers each attempt internally. On transient failure it
/// retries from scratch. The caller never sees a partial stream — either it receives all updates from a
/// successful attempt, or the exception propagates after all retries are exhausted.
/// </remarks>
public sealed class RetryingChatClient(IChatClient inner, RetryPolicy policy) : IChatClient
{
    static readonly Random Jitter = Random.Shared;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        int attempt = 0;

        while (true)
        {
            try
            {
                return await inner.GetResponseAsync(messages, options, cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < policy.MaxRetries)
            {
                TimeSpan delay = ComputeDelay(attempt, ex, policy);
                TagRetryAttempt(attempt + 1, delay, ex);
                await Task.Delay(delay, cancellationToken);
                attempt++;
            }
        }
    }

    /// <summary>
    /// Retries the full streaming request on transient failure. Each attempt is buffered
    /// internally; the caller receives updates only from the first successful attempt.
    /// </summary>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int attempt = 0;

        while (true)
        {
            List<ChatResponseUpdate> buffer = [];
            Exception? caught = null;

            try
            {
                await foreach (ChatResponseUpdate update in inner.GetStreamingResponseAsync(messages, options, cancellationToken))
                    buffer.Add(update);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            if (caught is null)
            {
                foreach (ChatResponseUpdate update in buffer)
                    yield return update;

                yield break;
            }

            if (!IsTransient(caught) || attempt >= policy.MaxRetries)
                throw caught;

            TimeSpan delay = ComputeDelay(attempt, caught, policy);
            TagRetryAttempt(attempt + 1, delay, caught);
            await Task.Delay(delay, cancellationToken);
            attempt++;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        inner.GetService(serviceType, serviceKey);

    public void Dispose() => inner.Dispose();

    static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException http =>
            http.StatusCode == HttpStatusCode.TooManyRequests ||
            ((int?)http.StatusCode >= 500 && (int?)http.StatusCode <= 599),
        TaskCanceledException => true,
        TimeoutException => true,
        IOException => true,
        _ => false
    };

    static TimeSpan ComputeDelay(int attempt, Exception ex, RetryPolicy policy)
    {
        // Honour Retry-After header for 429s when available.
        if (ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } httpEx)
        {
            // The OpenAI SDK doesn't surface Retry-After directly on the exception;
            // fall through to exponential backoff. A future improvement could intercept
            // the raw HttpResponseMessage via a DelegatingHandler to read the header.
            _ = httpEx;
        }

        double exponential = policy.BaseDelay.TotalSeconds * Math.Pow(2, attempt);
        double capped = Math.Min(exponential, policy.MaxDelay.TotalSeconds);
        double jitterFactor = 1.0 + (Jitter.NextDouble() * 0.4 - 0.2); // ±20%
        return TimeSpan.FromSeconds(capped * jitterFactor);
    }

    static void TagRetryAttempt(int attempt, TimeSpan delay, Exception ex)
    {
        Activity.Current?.SetTag("retry.attempt", attempt);
        Activity.Current?.SetTag("retry.delay_ms", (long)delay.TotalMilliseconds);
        Activity.Current?.SetTag("retry.reason", ex.GetType().Name);
    }
}
