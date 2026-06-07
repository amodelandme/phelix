# Context Compaction — Implementation Notes

**Date:** 2026-06-07
**Branch:** feature/context-compaction
**Spec:** spec.md

---

## What was built

All 15 steps from the spec, in order. No deviations from the design.

**New files:**
- `Phelix.Core/Session/ISessionStore.cs`
- `Phelix.Core/Session/SqliteSessionStore.cs`
- `Phelix.Core/Session/ICompactionPolicy.cs`
- `Phelix.Core/Session/TokenThresholdPolicy.cs`
- `Phelix.Core/Session/ISessionSummarizer.cs`
- `Phelix.Core/Session/ModelSessionSummarizer.cs`
- `Phelix.Core/Tools/SearchSessionTool.cs`
- 5 test files, 1 integration test

**Modified files:**
- `AgentOptions.cs` — `CompactionThresholdTokens` (default 40,000)
- `PhelixHost.cs` — wires store, policy, summarizer, registers `SearchSessionTool`, returns 5-tuple
- `Program.cs` — SQLite append + compaction check block after each turn
- `Phelix.Core.csproj` — `Microsoft.Data.Sqlite 9.0.5`

---

## Bugs found during manual testing

**FTS5 syntax error on special characters.** Queries like `"AGENTS.md"` caused `SQLite Error 1: 'fts5: syntax error near "."'` because FTS5 treats `.` as a token separator. Fixed in `SqliteSessionStore.SanitizeFtsQuery` — strips all non-alphanumeric, non-whitespace characters before passing to FTS5. Regression test added.

---

## Decisions made during implementation

**`SqliteSessionStore` implements both `ISessionStore` and `IDisposable`.** The store holds an open `SqliteConnection` for its lifetime. `IDisposable` is separate from `ISessionStore` — the store interface has no opinion about lifecycle. `Program.cs` casts to `IDisposable` to dispose it at session end.

**Token estimation does per-message integer division.** `150 / 4 = 37`, not `150.0 / 4 = 37.5`. Three messages of 150 chars sum to 111 characters' worth of truncation error. Test values use multiples of 4 to avoid this skewing assertions.

**`ModelSessionSummarizer` omits `modelId` parameter from the spec's constructor.** The spec showed `(IChatClient, ISessionStore, string modelId)` but `modelId` was never used in the implementation — the summarizer calls `GetResponseAsync` without `ChatOptions`, so the model is determined by however the `IChatClient` was constructed. Removed to avoid a dead parameter.
