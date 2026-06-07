# Session Schema Redesign — Feature Spec

**Status:** Approved  
**Phase:** Phase Queue  
**Date:** 2026-06-06

---

## Problem

`SessionEntry` captures only `UserMessage`, `AssistantMessage`, `ModelId`, and
`Timestamp`. Everything else that happens inside a turn is silently dropped:

- Tool calls (name, arguments, result) are never written
- Token usage is tracked in `AgentLoop` but never persisted
- `TurnExitReason` is available on `Turn` but absent from the log
- There is no session-level identity — the session ID lives only in the filename
- There is no way to correlate a tool call to its result when a model batches
  multiple calls in a single response
- A `bool Success` field is the proposed way to record tool outcomes — but a
  boolean carries no meaning at the call site and cannot be extended

The log is currently only useful as a human-readable chat transcript. It cannot
support RAG, agent diagnostics, token budgeting, or any replay scenario.

---

## Goal

Redesign the session schema so that a completed turn is fully recoverable from the
log. Every tool call, its result, token usage, exit reason, and session identity
must survive serialization. The on-disk format stays JSONL — one record per turn,
append-only, human-readable, no array wrapper.

---

## Non-Goals

- Changing the on-disk format (JSONL stays)
- Intermediate `ModelResponseEvent` records — turn-level sequencing of model
  responses within a tool loop is deferred until agent diagnostics are implemented
- Sensor result events — `SensorResultEvent` is reserved in the type hierarchy but
  not populated until the sensor pipeline is built (Phase 3)
- Session replay or compaction — separate roadmap items
- A session reader / query API — out of scope here; the log stays write-only

---

## Schema

### Top-level record: `TurnRecord`

One `TurnRecord` per turn, written as a single JSON line.

```csharp
public record TurnRecord(
    string TurnId,
    string SessionId,
    string UserMessage,
    string FinalAssistantMessage,
    string ModelId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    TurnExitReason ExitReason,
    UsageSummary Usage,
    IReadOnlyList<ToolCallRecord> ToolCalls
);
```

| Field | Source | Notes |
|---|---|---|
| `TurnId` | `Guid.NewGuid()` at turn start | Enables cross-log correlation |
| `SessionId` | Process-lifetime guid (currently in filename only) | Explicit on every record |
| `UserMessage` | Caller-supplied | Unchanged from current schema |
| `FinalAssistantMessage` | `turn.Response.Text` | Renamed from `AssistantMessage` for clarity |
| `ModelId` | `turn.Response.ModelId` | Unchanged |
| `StartedAt` | Recorded at `RunTurnAsync` entry | New; enables turn duration |
| `CompletedAt` | `turn.Timestamp` | Renamed from `Timestamp`; semantics made explicit |
| `ExitReason` | `turn.ExitReason` | Was dropped before; now persisted |
| `Usage` | `totalInputTokens` / `totalOutputTokens` in `AgentLoop` | Was tracked but never written |
| `ToolCalls` | Extracted from message list | Was silently dropped; now captured |

---

### Tool call record: `ToolCallRecord`

```csharp
public record ToolCallRecord(
    string CallId,
    string Name,
    string ArgumentsJson,
    string Result,
    ToolCallStatus Status
);
```

`CallId` comes from `FunctionCallContent.CallId` — already present on every tool
call dispatched by `AgentLoop`. It is the correlation key between a call and its
result when the model batches multiple tool calls in one response.

`ArgumentsJson` is stored as a raw JSON string, not a deserialized object. This
avoids a type explosion for heterogeneous tool arguments and keeps the record
schema stable across tool changes.

---

### Tool call status: `ToolCallStatus`

```csharp
public enum ToolCallStatus { Succeeded, Failed }
```

Replaces the `bool Success` that was proposed in the Good/Better analysis.
`ToolCallStatus.Succeeded` reads as documentation at the call site; `true` does not.
The enum extends cleanly — `Timeout`, `Cancelled`, `NotFound` can be added without
touching callers.

---

### Usage summary: `UsageSummary`

```csharp
public record UsageSummary(int InputTokens, int OutputTokens);
```

`AgentLoop` already accumulates `totalInputTokens` and `totalOutputTokens` across
all inner model calls in a turn. This record surfaces that data.

---

### Reserved event hierarchy (not populated until Phase 3)

The type hierarchy is defined now so that Phase 3 sensor work has a natural slot
without a schema change.

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SensorResultEvent), "sensorResult")]
public abstract record TurnEvent(DateTimeOffset Timestamp);

public sealed record SensorResultEvent(
    DateTimeOffset Timestamp,
    string SensorName,
    string Output,
    SensorStatus Status
) : TurnEvent(Timestamp);
```

```csharp
public enum SensorStatus { Passed, Failed, Skipped }
```

`TurnRecord` does not include a `TurnEvent` list yet. When sensors arrive, an
`IReadOnlyList<TurnEvent> Events` field is added and the tool call records can
optionally migrate into the event timeline. No existing field is removed.

---

## C# type locations

```
Phelix.Core/Session/
    TurnRecord.cs          — replaces SessionEntry.cs
    ToolCallRecord.cs      — new
    ToolCallStatus.cs      — new; enum
    UsageSummary.cs        — new
    TurnEvent.cs           — new; abstract base + SensorResultEvent (reserved)
    SensorStatus.cs        — new; enum (reserved for Phase 3)
    SessionLogger.cs       — updated; AppendAsync takes TurnRecord, not Turn + string
```

`SessionEntry.cs` is deleted. `SessionLogger` is updated in place.

---

## `SessionLogger` changes

`AppendAsync` currently takes a `Turn` and a raw `userMessage` string, and constructs
`SessionEntry` inside. After this change, the caller constructs a `TurnRecord` (or a
factory method does) and passes it directly. The logger is responsible only for
serialization and file I/O — not schema construction.

A `TurnRecord.FromTurn` factory method on `TurnRecord` keeps the construction logic
co-located with the type:

```csharp
public static TurnRecord FromTurn(
    Turn turn,
    string sessionId,
    string userMessage,
    string turnId,
    DateTimeOffset startedAt,
    IReadOnlyList<ToolCallRecord> toolCalls);
```

`AgentLoop` is the only place that has all the inputs needed to build `ToolCallRecord`
entries — it dispatches the calls and receives the results. The call site in
`Program.cs` will need to either pass tool calls through `Turn`, or the logger call
will move into `AgentLoop`. That decision is deferred to implementation; both options
are valid.

---

## Serialization

`System.Text.Json` with `PropertyNamingPolicy = CamelCase`. No change from current
approach. The `[JsonPolymorphic]` attributes on `TurnEvent` are inert until a
`TurnEvent` list is added to `TurnRecord` — they add no serialization overhead now.

On-disk line example (abbreviated):

```json
{
  "turnId": "a3f1...",
  "sessionId": "b9c2...",
  "userMessage": "add OpenTelemetry tracing",
  "finalAssistantMessage": "Done. I added ...",
  "modelId": "anthropic/claude-sonnet-4-6",
  "startedAt": "2026-06-06T14:01:00Z",
  "completedAt": "2026-06-06T14:01:12Z",
  "exitReason": "Completed",
  "usage": { "inputTokens": 1420, "outputTokens": 312 },
  "toolCalls": [
    {
      "callId": "call_01",
      "name": "ReadFileTool",
      "argumentsJson": "{\"path\":\"src/Program.cs\"}",
      "result": "using ...",
      "status": "Succeeded"
    }
  ]
}
```

---

## Files

### New

```
src/Phelix.Core/Session/TurnRecord.cs
src/Phelix.Core/Session/ToolCallRecord.cs
src/Phelix.Core/Session/ToolCallStatus.cs
src/Phelix.Core/Session/UsageSummary.cs
src/Phelix.Core/Session/TurnEvent.cs
src/Phelix.Core/Session/SensorStatus.cs
```

### Modified

```
src/Phelix.Core/Session/SessionLogger.cs   — AppendAsync signature change
src/Phelix.Cli/Program.cs                  — updated call site
src/Phelix.Core/Agent/AgentLoop.cs         — surface tool call records for logging
```

### Deleted

```
src/Phelix.Core/Session/SessionEntry.cs
```

### Tests

```
tests/Phelix.Core.Tests/Session/SessionLoggerTests.cs   — updated for new schema
```

---

## Implementation order

1. Define `ToolCallStatus`, `SensorStatus`, `UsageSummary` — pure enums and records
2. Define `TurnEvent` hierarchy — abstract base + `SensorResultEvent` (reserved)
3. Define `ToolCallRecord`
4. Define `TurnRecord` with `FromTurn` factory
5. Update `SessionLogger.AppendAsync` to accept `TurnRecord`
6. Update `AgentLoop` to surface tool call records (or thread them through `Turn`)
7. Update `Program.cs` call site
8. Delete `SessionEntry.cs`
9. Update `SessionLoggerTests`
