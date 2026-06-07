# Tool Output Truncation + Ephemeral Tool Pattern

**Status:** Approved  
**Phase:** Phase Queue  
**Date:** 2026-06-06

---

## Problem

Two compounding token sinks exist in the agent loop:

1. **Per-result blowup.** A single `bash` call or `read_file` on a large file can
   return tens of thousands of characters. That result is sent to the model
   verbatim, consuming a large fraction of the context window in one shot.

2. **History accumulation.** Raw tool call and tool result messages are kept in
   `conversationHistory` and re-sent to the model on every subsequent turn.
   Even with truncation, prior-turn tool exchanges compound across turns — the
   model pays for turn 1's tool output again on turns 2, 3, 4, and so on.

---

## Goals

1. Cap every tool result at a fixed character limit before it reaches the model
   or the session log.
2. Strip raw tool exchange messages from the history passed to the model after a
   turn completes, while preserving the full message list for session logging.

---

## Non-Goals

- Making `maxChars` configurable at runtime or in config (deferred)
- Rolling summarization of older turns (tracked separately as "Context compaction")
- Changing any tool's `ExecuteAsync` implementation

---

## Design

### Part 1 — Truncation helper

A private static method on `AgentLoop`:

```csharp
static string TruncateToolOutput(string result, int maxChars)
```

- If `result.Length <= maxChars`, return unchanged.
- Otherwise, return the first 80% of `maxChars` characters, then
  `\n... [X chars truncated] ...\n`, then the last 20% of `maxChars` characters.
- `maxChars` must be positive; throw `ArgumentOutOfRangeException` otherwise.

Named constant on `AgentLoop`:

```csharp
const int MaxToolOutputChars = 2000;
```

Applied to `result` immediately after `tool.ExecuteAsync` returns, before
`result` is used in either `toolCallRecords` or `toolResults`. Both the session
log and the model receive the truncated value — the log reflects what the model
actually saw.

The 80/20 head/tail split preserves the command header and output start (most
diagnostic value) while keeping the final lines (exit codes, last error) intact.

### Part 2 — Ephemeral tool pattern

`Turn` gains a second messages property:

```csharp
IReadOnlyList<ChatMessage> ContextMessages
```

`ContextMessages` is the pruned list used as history on the next turn.
`Messages` is the full record used by `SessionLogger`. Both are set in
`AgentLoop.RunTurnAsync` before the `Turn` is returned.

**What is pruned from `ContextMessages`:**
- All `ChatRole.Tool` messages (raw tool results)
- All assistant messages whose entire `Contents` collection consists of
  `FunctionCallContent` items (tool-dispatch requests with no text)

**What is kept:**
- The user message
- All assistant messages that contain text (the final reply and any
  intermediate text the model produced)

`Program.cs` passes `completedTurn.ContextMessages` as `conversationHistory`
on the next call instead of `completedTurn.Messages`.

---

## Contract

### `TruncateToolOutput`
- `result.Length <= maxChars` → returns `result` unchanged
- `result.Length > maxChars` → returns head (80%) + notice + tail (20%)
- Notice format: `\n... [X chars truncated] ...\n` where X is the number of
  removed characters
- `maxChars <= 0` → throws `ArgumentOutOfRangeException`

### `Turn.ContextMessages`
- Contains no `ChatRole.Tool` messages
- Contains no assistant messages composed entirely of `FunctionCallContent`
- Always contains the user message and the final assistant text reply
- Order is preserved relative to `Messages`

---

## Tests

### Truncation
- `TruncateToolOutput_ShortResult_ReturnsUnchanged`
- `TruncateToolOutput_LongResult_ReturnsHeadAndTail`
- `TruncateToolOutput_LongResult_NoticeContainsTruncatedCharCount`
- `TruncateToolOutput_MaxCharsZero_ThrowsArgumentOutOfRangeException`

### Ephemeral tool pattern
- `RunTurnAsync_ContextMessages_ExcludesToolRoleMessages`
- `RunTurnAsync_ContextMessages_ExcludesAssistantToolCallMessages`
- `RunTurnAsync_ContextMessages_RetainsUserAndFinalAssistantMessages`
