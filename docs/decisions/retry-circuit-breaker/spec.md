# Retry / Circuit Breaker — Spec

## Problem

A 429 (rate limit) or transient network error kills the session immediately. There is no retry logic anywhere in the call chain. The exception propagates through `AgentLoop.RunTurnAsync` → `Program.cs` → printed as `Error: ...` and the turn is lost.

Two call sites are affected:
- `AgentLoop` — `chatClient.GetStreamingResponseAsync` (`AgentLoop.cs:91`)
- `ModelSessionSummarizer` — `chatClient.GetResponseAsync` (`ModelSessionSummarizer.cs:53`)

---

## Decision

Implement a retry-with-exponential-backoff `IChatClient` middleware and insert it into the `ChatClientBuilder` pipeline in `PhelixHost.cs`. No circuit breaker — the failure scenarios we care about are transient and session-scoped; a circuit breaker adds state and complexity without meaningful benefit at this scale.

---

## Approach

### Transient errors

Retry only on errors that are clearly transient:

| Condition | Source |
|---|---|
| HTTP 429 | `HttpRequestException` with status 429, or OpenAI SDK exception containing that status |
| HTTP 5xx | `HttpRequestException` with status 500–599 |
| `TaskCanceledException` / `TimeoutException` | Network timeout |
| `IOException` | Socket-level failure |

Do **not** retry on 4xx (except 429) — those indicate a caller error and will not resolve on retry.

### Backoff

Exponential backoff with jitter:

```
delay = min(BaseDelay * 2^attempt, MaxDelay) + random jitter (±20%)
```

Defaults:
- `MaxRetries`: 4
- `BaseDelay`: 2 s
- `MaxDelay`: 60 s

For 429 responses, honour the `Retry-After` header if present and cap at `MaxDelay`.

### Streaming

`GetStreamingResponseAsync` returns an `IAsyncEnumerable` — the HTTP request goes out on the first `MoveNextAsync()`, so a 429 or network error surfaces as a discrete request failure, not a partial read. Re-sending is safe.

The concern is *duplicated output*, not *retrying*. If `onChunk` has already forwarded text to the terminal, a retry would print the same content again.

**Strategy:** suppress `onChunk` during retry attempts (pass `null` to the inner call for all attempts except the last successful one). Retry on any transient error regardless of whether chunks were yielded — the output guard is the callback suppression, not an abort on first chunk.

### Where the middleware lives

New file: `src/Phelix.Core/Agent/RetryingChatClient.cs`

Registered in `PhelixHost.Build()` (`PhelixHost.cs:70`):

```csharp
IChatClient chatClient = new ChatClientBuilder(
    openAiClient.GetChatClient(activeModel.ModelId).AsIChatClient())
    .Use(inner => new RetryingChatClient(inner, retryPolicy))
    .UseOpenTelemetry(loggerFactory: null, sourceName: PhelixTelemetry.SourceName)
    .Build();
```

Retry wraps the inner client before telemetry so retry attempts are individually traced.

### Configuration

Add a `RetryPolicy` record to `Phelix.Core`:

```csharp
public sealed record RetryPolicy(
    int MaxRetries = 4,
    TimeSpan BaseDelay = default,   // 2 s
    TimeSpan MaxDelay = default);   // 60 s
```

Different models have different rate limits — a fast cheap model used for compaction summaries can tolerate aggressive retries; a premium model may need different backoff. `RetryPolicy` therefore lives on `ModelConfig` as an optional override:

```csharp
public RetryPolicy? Retry { get; init; }
```

`PhelixConfig` also gains a top-level `RetryPolicy? Retry` property as the global default. Resolution order at build time:

1. `ModelConfig.Retry` if set
2. `PhelixConfig.Retry` if set
3. `RetryPolicy` defaults (4 retries, 2 s base, 60 s max)

Falls back gracefully when neither is present — no breaking change to existing `config.yaml`.

---

## What `RetryingChatClient` does

Implements `IChatClient`. For each call:

1. Loop up to `MaxRetries + 1` attempts.
2. On success, return immediately.
3. On exception:
   a. If not transient → rethrow without retrying.
   b. If transient and attempts remain → compute delay (honouring `Retry-After` for 429), `await Task.Delay(delay, cancellationToken)`, then retry.
   c. If transient and no attempts remain → rethrow.
4. For `GetStreamingResponseAsync`: the middleware does not expose the streaming callback directly — that is `AgentLoop`'s concern. The middleware retries the full `IAsyncEnumerable` from scratch on transient failure. Because `onChunk` is suppressed on failed attempts (the caller passes it only after the middleware returns successfully), no duplicate output reaches the terminal. Concretely: the middleware buffers updates internally on each attempt; if an attempt succeeds, the buffered updates are yielded to the caller. This keeps the retry loop entirely inside the middleware without leaking retry state upward.

---

## Telemetry

On each retry attempt, tag the current activity:

```
retry.attempt = <n>
retry.delay_ms = <ms>
retry.reason = <exception type>
```

Use `Activity.Current` — no new spans.

---

## Out of scope

- Circuit breaker state machine
- Retry on `ModelSessionSummarizer` separately — it goes through the same `IChatClient` pipeline so it gets retry for free

---

## Files changed

| File | Change |
|---|---|
| `src/Phelix.Core/Agent/RetryingChatClient.cs` | New — the middleware |
| `src/Phelix.Core/Config/RetryPolicy.cs` | New — policy record |
| `src/Phelix.Core/Config/ModelConfig.cs` | Add `RetryPolicy? Retry` property |
| `src/Phelix.Core/Config/PhelixConfig.cs` | Add `RetryPolicy? Retry` global default property |
| `src/Phelix.Core/Config/ConfigLoader.cs` | Map `Retry` section from YAML; resolve effective policy at load time |
| `src/Phelix.Cli/PhelixHost.cs` | Wire middleware into `ChatClientBuilder`; pass resolved policy |
| `tests/Phelix.Core.Tests/Agent/RetryingChatClientTests.cs` | New — unit tests |
