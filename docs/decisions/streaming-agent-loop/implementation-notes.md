# Streaming Agent Loop — Implementation Notes

**Status:** Complete  
**Date:** 2026-06-03  
**Branch:** feature/streaming-agent-loop

---

## What was built

Replaced `GetResponseAsync` with `GetStreamingResponseAsync` in `AgentLoop.RunTurnAsync`
so the terminal receives and renders tokens as the model generates them. Added
`SessionLoggerTests` covering a two-turn mock session and standalone JSONL line
parseability.

---

## Files changed

| File | Change |
|---|---|
| `src/Phelix.Core/Agent/AgentLoop.cs` | Modified — streaming loop, chunk forwarding, `ToChatResponse()` aggregation |
| `tests/Phelix.Core.Tests/Session/SessionLoggerTests.cs` | New — mock session logger tests |

---

## Decisions made during implementation

### Manual iteration over `AddMessagesAsync`

MEAI provides `AddMessagesAsync`, which consumes the stream and appends the
reconstructed messages directly to a list. It was not used here because it
discards the `ChatResponse` — there is no way to recover `Usage`, `FinishReason`,
or `ModelId` from it. The manual pattern was chosen instead:

```csharp
List<ChatResponseUpdate> updates = new();

await foreach (ChatResponseUpdate update in chatClient.GetStreamingResponseAsync(...))
{
    if (onChunk is not null && !string.IsNullOrEmpty(update.Text))
        await onChunk(update.Text);

    updates.Add(update);
}

ChatResponse response = updates.ToChatResponse();
```

`ToChatResponse()` (the synchronous extension on `IEnumerable<ChatResponseUpdate>`)
reconstructs the full `ChatResponse` including messages, usage, and finish reason.
The rest of the loop is unchanged — tool call dispatch and telemetry all come off
the aggregated response as before.

### `onChunk` is not called during tool-call turns

When the model emits tool call content, `ChatResponseUpdate.Text` is null or empty
on those updates. The `!string.IsNullOrEmpty(update.Text)` guard means `onChunk`
is never invoked mid-tool-turn. This is the correct behavior — intermediate tool
call content is not meaningful to the terminal renderer.

### `ToChatResponse()` handles message reconstruction

`ToChatResponse()` uses `MessageId` on each update to determine message boundaries
and coalesces contiguous `TextContent` items. This means tool call metadata
(`FunctionCallContent`) is correctly reconstructed in the aggregated message,
preserving the existing dispatch logic in full.

### Streaming does not affect telemetry

Token usage is reported on the aggregated `ChatResponse`, not on individual
updates. The accumulation pattern (`totalInputTokens`, `totalOutputTokens`) is
unchanged. The OTel `gen_ai.chat` spans emitted by the MEAI middleware still
wrap the full streaming call, so Jaeger traces look identical to the pre-streaming
traces structurally.

### Session logger tests use a fake `ChatResponse`

`SessionLogger.AppendAsync` already accepted an optional `filePath` parameter for
testing. The tests construct `Turn` and `ChatResponse` directly — no network, no
mock framework. `ChatResponse` takes a list of `ChatMessage` objects in its
constructor, which is sufficient to populate `Response.Text` and `Response.ModelId`
for serialization. The test writes two turns to a temp file and asserts both lines
are independently parseable JSON objects.

---

## What was deferred

- **Streaming during tool-call intermediate turns** — the model's tool call
  requests are not streamed to the terminal. Tool responses are fire-and-forget
  from the user's perspective, so this is intentional for now.
- **Streaming cancellation on partial chunks** — `CancellationToken` is forwarded
  to `GetStreamingResponseAsync`, so the stream can be cancelled. Handling a
  partially-rendered line in `TerminalRenderer` on cancellation is not addressed.
