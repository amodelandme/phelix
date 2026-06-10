# Retry / Circuit Breaker — Implementation Notes

## What was built

`RetryingChatClient` — an `IChatClient` middleware registered in the `ChatClientBuilder` pipeline in `PhelixHost.cs` between the OpenAI base client and the OpenTelemetry middleware. Both `AgentLoop` and `ModelSessionSummarizer` get retry for free through the shared pipeline.

## Deviations from spec

None. Implementation matched the spec exactly.

## Key decisions made during implementation

**`Metadata` property dropped from `RetryingChatClient`.**
`Phelix.Core` doesn't reference `Microsoft.Extensions.AI` directly — only `Phelix.Core.Tests` does. `ChatClientMetadata` is not in scope for the core library, so the delegating `Metadata` property was omitted. The `IChatClient` contract in this SDK version doesn't require it.

**Streaming buffer approach confirmed correct.**
The middleware buffers `ChatResponseUpdate` items from each attempt internally and only yields them to the caller after a complete successful iteration. This guarantees no partial output escapes on a failed attempt, without any coordination with `AgentLoop`'s `onChunk` callback.

**`Retry-After` header left as a comment / future improvement.**
The OpenAI SDK does not surface the `Retry-After` header on `HttpRequestException`. Accessing it would require a custom `DelegatingHandler` at the HTTP transport layer. The exponential backoff handles 429 correctly without it; honouring the header would only tighten the timing. Left as a `// future improvement` comment in `ComputeDelay`.

## Files changed

| File | Change |
|---|---|
| `src/Phelix.Core/Config/RetryPolicy.cs` | New — policy record with `MaxRetries`, `BaseDelay`, `MaxDelay` |
| `src/Phelix.Core/Config/ModelConfig.cs` | Added `RetryPolicy? Retry` per-model override |
| `src/Phelix.Core/Config/PhelixConfig.cs` | Added `RetryPolicy? Retry` global default |
| `src/Phelix.Core/Config/FileConfigProvider.cs` | Added `RawRetryPolicy` deserialization target and `MapRetryPolicy` helper |
| `src/Phelix.Core/Config/ConfigLoader.cs` | Added `ResolveRetryPolicy` — resolves model → global → hardcoded defaults |
| `src/Phelix.Core/Agent/RetryingChatClient.cs` | New — the middleware |
| `src/Phelix.Cli/PhelixHost.cs` | Wires `RetryingChatClient` into `ChatClientBuilder` before telemetry |
| `tests/Phelix.Core.Tests/Agent/RetryingChatClientTests.cs` | New — 13 unit tests |

## Test coverage

13 tests across both call paths:

- First-attempt success (non-streaming and streaming)
- Retry-then-succeed after transient failures (non-streaming and streaming)
- Exhausted retries propagate the exception (non-streaming and streaming)
- Non-transient errors rethrow immediately without retry
- All retried HTTP status codes: 429, 500, 502, 503
- HTTP 400 confirmed non-retryable
- No partial streaming updates emitted from failed attempts
