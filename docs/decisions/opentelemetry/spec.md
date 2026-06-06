# OpenTelemetry Tracing — Feature Spec

**Status:** Approved  
**Phase:** 2.5 (after tool dispatch, before sensors)  
**Date:** 2026-06-02

---

## Problem

Phelix has no observability. When a long task runs, there is no way to answer:

- How long did this turn take?
- How many model round-trips did it require?
- Which tool call was slow?
- How many tokens did this task consume?
- Did any tool calls fail silently?

Without this signal, debugging agent behavior is guesswork. For .NET shops evaluating
Phelix for production use, missing observability is a blocker.

---

## Goal

Emit structured traces for every agent turn and tool call using the OpenTelemetry
standard. Wire model-call telemetry through the existing `Microsoft.Extensions.AI`
middleware. Export via OTLP to any compatible backend (Jaeger, Grafana Tempo, etc.).

Tracing must be **zero-cost when disabled** — no listener attached means no
allocations, no behavior change.

---

## Non-Goals

- Metrics (counters, histograms) — future phase
- Log correlation (linking `SessionLogger` entries to trace IDs) — future phase
- Distributed tracing across multiple Phelix processes — not applicable to MVP
- A bundled local backend — the user provides their own OTLP target

---

## Design

### Span model

Two span types are introduced. A third is provided for free by M.E.AI middleware.

#### `phelix.agent.turn`

Root span. One per call to `AgentLoop.RunTurnAsync`.

| Attribute | Type | Description |
|---|---|---|
| `phelix.turn.model_id` | string | Model identifier from `AgentOptions` |
| `phelix.turn.tool_turns` | int | Number of tool-call roundtrips completed |
| `gen_ai.usage.input_tokens` | int | Total input tokens across all model calls this turn |
| `gen_ai.usage.output_tokens` | int | Total output tokens across all model calls this turn |

Status is set to `Error` if an unhandled exception escapes the turn.

#### `phelix.tool.call`

Child span. One per tool invocation within a turn. Parented under `phelix.agent.turn`
via ambient `Activity.Current`.

| Attribute | Type | Description |
|---|---|---|
| `phelix.tool.name` | string | `ITool.Name` of the invoked tool |
| `phelix.tool.success` | bool | `true` if the tool returned a result, `false` if it returned an error string |
| `phelix.tool.error` | string | Error message, only present when `phelix.tool.success` is `false` |

#### `gen_ai.chat` (automatic)

Emitted by `Microsoft.Extensions.AI`'s `UseOpenTelemetry()` middleware. One per
`GetResponseAsync` call. Parented under `phelix.agent.turn` automatically via
ambient context. Carries `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`,
`gen_ai.request.model`, and `gen_ai.response.finish_reason`.

No code required in `AgentLoop` to produce these spans.

### Ambient context and automatic parenting

`Activity.Current` is `AsyncLocal` — it flows through `await` boundaries. When
`AgentLoop.RunTurnAsync` starts `phelix.agent.turn`, that span becomes the ambient
current. Any span started downstream (including `gen_ai.chat` inside the M.E.AI
middleware) is automatically parented to it. No explicit parent wiring is needed.

### Zero-cost when disabled

`ActivitySource.StartActivity` returns `null` when no listener is attached. All
tracing code uses the null-conditional operator (`?.`) throughout, so every
`SetTag`, `SetStatus`, and `Dispose` call is a no-op when tracing is off. The
agent loop runs identically whether an OTLP exporter is configured or not.

---

## Files

### New

```
src/Phelix.Core/Telemetry/PhelixTelemetry.cs
```

Owns the single `ActivitySource` instance. Defines all span name and tag name
constants. No logic — purely a named source and a schema registry.

### Modified

```
src/Phelix.Core/Agent/AgentLoop.cs
```

- Start `phelix.agent.turn` span at the top of `RunTurnAsync`
- Start and stop `phelix.tool.call` child span around each `tool.ExecuteAsync` call
- Set token usage attributes on the turn span after each model response
- Set turn span status to `Error` on unhandled exception

```
src/Phelix.Cli/Program.cs
```

- Add `OpenTelemetry` SDK builder with `AddSource("Phelix")`
- Add OTLP exporter pointed at `OTEL_EXPORTER_OTLP_ENDPOINT` env var
- Wrap `IChatClient` construction with `.UseOpenTelemetry()`

### New packages

| Package | Project | Purpose |
|---|---|---|
| `OpenTelemetry.Extensions.Hosting` | `Phelix.Cli` | SDK bootstrap and listener registration |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | `Phelix.Cli` | OTLP export |

`Phelix.Core` gains **no new NuGet dependencies**. `System.Diagnostics.ActivitySource`
is in-box in .NET 5+.

---

## Configuration

Tracing is opt-in via environment variable. If `OTEL_EXPORTER_OTLP_ENDPOINT` is not
set, the exporter is not registered and no spans are exported. The `ActivitySource`
still exists but `StartActivity` returns null (no listener), so overhead is zero.

```bash
# Enable tracing to a local Jaeger instance
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
phelix "refactor the OrdersService"
```

The service name sent to the backend is `"phelix"`, set via
`OTEL_SERVICE_NAME` or hardcoded as the default in `Program.cs`.

---

## What a trace looks like

```
[turn]  phelix.agent.turn                        2.3s
          phelix.turn.model_id    = "google/gemma-4-31b-it:free"
          phelix.turn.tool_turns  = 2
          gen_ai.usage.input_tokens  = 3840
          gen_ai.usage.output_tokens = 492

  [model] gen_ai.chat                            780ms
            gen_ai.usage.input_tokens  = 1204
            gen_ai.usage.output_tokens = 312

  [tool]  phelix.tool.call                       4ms
            phelix.tool.name     = "read_file"
            phelix.tool.success  = true

  [tool]  phelix.tool.call                       3ms
            phelix.tool.name     = "read_file"
            phelix.tool.success  = true

  [model] gen_ai.chat                            1100ms
            gen_ai.usage.input_tokens  = 2636
            gen_ai.usage.output_tokens = 180
```

---

## Implementation order

1. `PhelixTelemetry.cs` — source and constants only, no logic
2. `AgentLoop.cs` — instrument turn span and tool call spans
3. `Program.cs` — register SDK, exporter, and M.E.AI middleware
4. Manual smoke test against a local OTLP backend

---

## Out of scope for this spec

- `SessionLogger` correlation (trace ID in the JSONL record)
- Per-sensor spans (Phase 3)
- Metrics (Phase 4+)
