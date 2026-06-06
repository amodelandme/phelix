# Pre-TUI Cleanup — Implementation Notes

**Branch:** `chore/pre-tui-cleanup`  
**Date:** 2026-06-05  
**Spec:** `docs/decisions/pre-tui-cleanup/spec.md`

---

## What was done

Five items delivered as a single commit. All 38 existing tests pass unchanged.

### Item 1 — Indentation fixed in `PhelixTelemetry.cs`

`Program.cs` indentation was already corrected in a prior session (#7). `PhelixTelemetry.cs`
still had all content indented with leading spaces — corrected. Top-level declarations
now start at column 0. Zero logic change.

### Item 2 — `MaxturnsDefault` → `MaxTurnsDefault` in `AgentOptions`

One-line rename. Both the constant and the property it backs now follow PascalCase
consistently.

### Item 3 — `AgentLoop` returns complete history

The `messages.Add(assistantMessage)` call was duplicated across both branches of
the dispatch loop — once before the tool-call loop continued, once inside the
stop branch. Consolidated to a single append before the branch test, so both
paths append identically.

`Turn.Messages` is now complete history including the final assistant reply.
`Program.cs` assigns it directly:

```csharp
conversationHistory = completedTurn.Messages;
```

`conversationHistory` type changed from `List<ChatMessage>` to `IReadOnlyList<ChatMessage>`
— the REPL only passes it into `RunTurnAsync`, which already accepts `IReadOnlyList`.
No cast needed anywhere.

### Item 4 — `PhelixHost` extracts bootstrapping from `Program.cs`

New file: `src/Phelix.Cli/PhelixHost.cs`

`PhelixHost.Build()` owns OTel setup, `OpenAIClient` / `IChatClient` construction,
`AgentOptions`, and `ToolRegistry` population. Returns `(AgentLoop, TracerProvider?)`.

`Program.cs` is now the REPL loop and nothing else — 35 lines.

### Item 5 — `ToolRegistry` caches `AITool` list at registration time

`Register` now calls `tool.ToAITool()` and appends the result to a `List<AITool>`
field. `ToAITools()` returns that cached list. Schema reflection via
`AIFunctionFactory.Create` is paid once at startup, not on every model call.

---

## Decisions made during implementation

- `conversationHistory` declared as `IReadOnlyList<ChatMessage>` rather than casting
  `Turn.Messages` to `List<ChatMessage>`. The REPL has no need to mutate the list —
  using the narrower interface is both correct and enforces that intent.

- Two diagnostics (`new List<>()` → collection expression `[]`) were fixed inline
  in `AgentLoop.cs` and `Program.cs` while the files were open. Kept the codebase
  warning-clean.

---

## Verified

- `dotnet build phelix.slnx` → 0 errors, 0 warnings
- `dotnet test` → 38/38 passing
- Manual end-to-end: single turn, two-turn history, one tool call — all correct
