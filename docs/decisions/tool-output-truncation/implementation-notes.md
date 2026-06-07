# Tool Output Truncation + Ephemeral Tool Pattern: Implementation Notes

**Date:** 2026-06-06

---

## Part 1 — Truncation helper

`AgentLoop.TruncateToolOutput(string result, int maxChars)` — public static method.

- Called immediately after `tool.ExecuteAsync` returns, before the result is
  stored in `toolCallRecords` or wrapped in `FunctionResultContent`.
- `MaxToolOutputChars = 2000` — named constant on `AgentLoop`.
- Head/tail split: first 80% of `maxChars` characters, then
  `\n... [X chars truncated] ...\n`, then the last 20%.
- Both the session log and the model receive the truncated value.

## Part 2 — Ephemeral tool pattern

`Turn` gained a second messages property: `ContextMessages`.

- `Messages` — the full record, used by `SessionLogger`. Unchanged.
- `ContextMessages` — the pruned list passed as history to the next turn.

`AgentLoop.BuildContextMessages` filters `messages` before return:
- Drops all `ChatRole.Tool` messages (raw tool results).
- Drops all assistant messages whose entire `Contents` collection is
  `FunctionCallContent` (tool-dispatch requests with no text).
- Keeps user messages and any assistant message containing `TextContent`.

`Program.cs` now passes `completedTurn.ContextMessages` as `conversationHistory`
instead of `completedTurn.Messages`.

## Files touched

- `src/Phelix.Core/Agent/AgentLoop.cs` — truncation helper, `BuildContextMessages`, `MaxToolOutputChars`
- `src/Phelix.Core/Agent/Turn.cs` — added `ContextMessages` property
- `src/Phelix.Cli/Program.cs` — passes `ContextMessages` as history
- `tests/Phelix.Core.Tests/Agent/AgentLoopTests.cs` — 8 new tests
- `tests/Phelix.Core.Tests/Agent/Fakes.cs` — `FakeChatClient`, `FakeToolRegistry`, `FakeTool`
- `tests/Phelix.Core.Tests/Session/SessionLoggerTests.cs` — updated `BuildFakeTurn` for new `Turn` constructor
