# OpenTelemetry Tracing — Implementation Notes

**Status:** Complete  
**Date:** 2026-06-02  
**Branch:** feature/readme-updates

---

## What was built

Structured tracing for every agent turn and tool call, verified end-to-end against
a local Jaeger instance. All spans and tags from the spec landed correctly.

---

## Files changed

| File | Change |
|---|---|
| `src/Phelix.Core/Telemetry/PhelixTelemetry.cs` | New — `ActivitySource` and all span/tag name constants |
| `src/Phelix.Core/Agent/AgentLoop.cs` | Modified — turn span, tool call spans, token accumulation |
| `src/Phelix.Cli/Program.cs` | Modified — OTel SDK registration, OTLP exporter, `ChatClientBuilder` pipeline |
| `src/Phelix.Cli/Phelix.Cli.csproj` | Modified — added `OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Exporter.OpenTelemetryProtocol` |

---

## Decisions made during implementation

### `ChatClientBuilder` constructor form is required

The spec called for `.AsIChatClient().UseOpenTelemetry()`. This does not compile.
`.AsIChatClient()` has two overloads — the zero-argument form returns `IChatClient`,
not `ChatClientBuilder`. `UseOpenTelemetry` is an extension on `ChatClientBuilder`.

The working pattern is:

```csharp
IChatClient chatClient = new ChatClientBuilder(
        openAiClient.GetChatClient(modelId).AsIChatClient())
    .UseOpenTelemetry(loggerFactory: null, sourceName: PhelixTelemetry.SourceName)
    .Build();
```

### `UseOpenTelemetry` signature differs from documentation

The M.E.AI 10.4.0 signature is:

```csharp
UseOpenTelemetry(ILoggerFactory? loggerFactory, string? sourceName, Action<OpenTelemetryChatClient>? configure)
```

The `configure` named parameter exists but is positional third — pass `null` for
`loggerFactory` and `PhelixTelemetry.SourceName` for `sourceName`. Passing
`SourceName` here ensures the M.E.AI middleware emits `gen_ai.chat` spans under
the same `ActivitySource` the OTel SDK is already listening to.

### Token accumulation is across all model calls in a turn

A multi-tool turn involves multiple `GetResponseAsync` calls. Token counts on the
turn span are the sum across all of them, accumulated in `totalInputTokens` and
`totalOutputTokens` locals and written to the span at each return point.

### `using` scope for tool call spans is intentional

Tool call spans use an explicit `using` block rather than a top-of-method
declaration. This scopes each span to exactly the `ExecuteAsync` call it wraps.
A top-of-loop declaration would produce only one span covering the last tool
call's duration, not one span per tool.

### Tracing is zero-cost when disabled

`OTEL_EXPORTER_OTLP_ENDPOINT` not being set means no `TracerProvider` is built,
no listener is attached to `PhelixTelemetry.Source`, and `StartActivity` returns
`null` throughout. All tracing code uses `?.` so every tag and status call is a
no-op. No behavior change, no allocations.

---

## Verified in Jaeger

Trace from a single turn with one `read_file` tool call:

```
phelix.agent.turn
  gen_ai.usage.input_tokens  = 1611
  gen_ai.usage.output_tokens = 997
  phelix.turn.model_id       = moonshotai/kimi-k2.6:free
  phelix.turn.tool_turns     = 1

  gen_ai.chat  (11.01s)
    gen_ai.operation.name         = chat
    gen_ai.provider.name          = openai
    gen_ai.request.model          = moonshotai/kimi-k2.6:free
    gen_ai.response.finish_reasons = tool_calls
    gen_ai.tool.definitions       = [read_file function schema]

  phelix.tool.call  (6.4ms)
    phelix.tool.name    = read_file
    phelix.tool.success = true

  gen_ai.chat  (9.06s)
    gen_ai.response.finish_reasons = stop
```

Parent/child nesting is correct. Ambient `AsyncLocal` context wired the
`gen_ai.chat` spans under `phelix.agent.turn` without any explicit parent
references in `AgentLoop`.

---

## What was deferred

These items are out of scope for this feature and tracked in the spec as non-goals:

- `SessionLogger` correlation — writing the trace ID into the JSONL session record
- Per-sensor spans — Phase 3, when the sensor pipeline is built
- Metrics — Phase 4+
