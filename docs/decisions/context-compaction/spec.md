# Context Compaction and Session Continuity — Feature Spec

**Status:** Draft  
**Phase:** Phase Queue  
**Date:** 2026-06-07

---

## Problem

`conversationHistory` is an unbounded list. Every turn appends new messages and
the full list is sent to the model on every subsequent call. Two forces compound
this:

1. **Turn accumulation.** Each turn adds at least two messages (user + assistant).
   Long sessions push history into the tens of thousands of tokens.

2. **Session loss on compaction.** When the provider compacts the context window
   server-side, or when the process restarts, the agent loses all context — what
   it was doing, what it decided, which files it touched.

These are not separate problems. They share a root cause: the harness has no
durable record of what happened, so it cannot recover or summarize when history
becomes too large.

---

## Goals

1. Persist every completed turn to SQLite in real time so that session state
   survives process restarts and compaction events.
2. Detect when `conversationHistory` crosses a token threshold and compact it:
   replace the full history with a single summary message reconstructed from
   SQLite, not from in-memory state.
3. Expose a `search_session` tool so the model can query detailed session history
   on demand after compaction.
4. Keep `AgentLoop`, `Program.cs`, and all existing types unchanged except at
   explicit wiring points.

---

## Non-Goals

- Resuming a previous session by ID (deferred — the infrastructure built here
  makes it straightforward later)
- Vector / semantic search over session history (deferred — FTS5 handles the
  immediate need)
- Replacing the JSONL session log (it stays; SQLite is additive)
- Changing the `IChatClient` abstraction or provider wiring
- Automatic summarization without a model call (out of scope — rule-based
  summarization is a different tradeoff)

---

## Design

### Principles

**No coupling through concrete types.** Every new component is defined behind an
interface. `AgentLoop` and `Program.cs` never import `Microsoft.Data.Sqlite`.
SQLite is an implementation detail of `Phelix.Core.Session`, not a dependency of
the agent.

**Normal path unchanged.** Turns that do not trigger compaction behave exactly as
today. The only new operation on the normal path is a fire-and-forget SQLite write
after each turn — same position as the existing JSONL write.

**Compaction is a policy, not a mechanism.** Whether and when to compact is
decided by `ICompactionPolicy`. The mechanism that does the compaction
(`ISessionSummarizer`) is separate. Either can be swapped without touching the
other.

**The model call in the summarizer is injectable.** `ISessionSummarizer` takes an
`IChatClient`. Tests pass a fake; production passes the same client used by
`AgentLoop`. No hidden model calls.

---

### Component map

```
Phelix.Core/Session/
    ISessionStore.cs            — read/write interface over durable turn storage
    SqliteSessionStore.cs       — ISessionStore backed by Microsoft.Data.Sqlite
    ICompactionPolicy.cs        — decides whether to compact given a message list
    TokenThresholdPolicy.cs     — ICompactionPolicy: fires at N estimated tokens
    ISessionSummarizer.cs       — produces a summary string from stored turns
    ModelSessionSummarizer.cs   — ISessionSummarizer: calls the model to summarize
    SearchSessionTool.cs        — ITool: FTS5 query over stored tool outputs

Phelix.Core/Agent/
    AgentOptions.cs             — gains CompactionThresholdTokens (default: 40_000)
    AgentLoop.cs                — unchanged internally; compaction is a caller concern

Phelix.Cli/
    PhelixHost.cs               — wires ISessionStore, ICompactionPolicy,
                                   ISessionSummarizer, SearchSessionTool
    Program.cs                  — gains compaction check between turns
```

`AgentLoop` does not know that compaction exists. It receives a `conversationHistory`
list and returns a `Turn`. What the caller does with that list between turns —
including replacing it — is the caller's business.

---

### Interfaces

#### `ISessionStore`

```csharp
public interface ISessionStore
{
    Task AppendAsync(TurnRecord record, CancellationToken ct = default);

    Task<IReadOnlyList<TurnRecord>> GetTurnsAsync(
        string sessionId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ToolCallRecord>> SearchToolOutputsAsync(
        string query,
        int maxResults = 5,
        CancellationToken ct = default);
}
```

`AppendAsync` replaces (or runs alongside) `SessionLogger.AppendAsync` as the
durable write. `GetTurnsAsync` is used only by `ISessionSummarizer` —
`AgentLoop` never calls it. `SearchToolOutputsAsync` backs `SearchSessionTool`.

#### `ICompactionPolicy`

```csharp
public interface ICompactionPolicy
{
    bool ShouldCompact(IReadOnlyList<ChatMessage> history);
}
```

Single method, no async. The decision is synchronous and cheap — it reads token
estimates from the message list, no I/O. Returning `true` means the caller must
compact before the next turn.

#### `ISessionSummarizer`

```csharp
public interface ISessionSummarizer
{
    Task<string> SummarizeAsync(
        string sessionId,
        CancellationToken ct = default);
}
```

Returns a plain string — the summary text, not a `ChatMessage`. The caller wraps
it in a message. This keeps the summarizer ignorant of `Microsoft.Extensions.AI`
message types; a future impl that builds summaries from a graph or index does not
need to import that package.

---

### Data layer — `SqliteSessionStore`

Two tables:

**`turns`** — one row per `TurnRecord`. Columns map 1:1 to `TurnRecord` fields.
`tool_calls` is stored as JSON text (the existing `IReadOnlyList<ToolCallRecord>`
serialized with `System.Text.Json`).

**`tool_outputs`** — one row per `ToolCallRecord`, extracted from each turn on
write. This is the FTS5-indexed table.

```sql
CREATE VIRTUAL TABLE tool_outputs USING fts5(
    turn_id,
    session_id,
    tool_name,
    arguments_json,
    result
);
```

`SqliteSessionStore` opens one connection per store instance. The database file
lives at `~/.phelix/sessions/<sessionId>.db`. One file per session — matches the
existing JSONL naming convention and makes session-scoped cleanup trivial.

`AppendAsync` uses a transaction to write to both `turns` and `tool_outputs`
atomically. If the write fails partway through, neither table has a partial record.

---

### Compaction policy — `TokenThresholdPolicy`

```csharp
public sealed class TokenThresholdPolicy(int thresholdTokens) : ICompactionPolicy
{
    public bool ShouldCompact(IReadOnlyList<ChatMessage> history)
    {
        int estimated = history.Sum(m => EstimateTokens(m));
        return estimated >= thresholdTokens;
    }

    static int EstimateTokens(ChatMessage message) =>
        message.Contents
            .OfType<TextContent>()
            .Sum(t => t.Text?.Length ?? 0) / 4;
}
```

Token estimation uses the standard characters-divided-by-4 heuristic. It is
deliberately imprecise — the goal is to fire well before the hard context limit,
not at exactly the right token count. `thresholdTokens` is set from
`AgentOptions.CompactionThresholdTokens` (default 40,000 tokens ≈ half a 80K
context window).

The divisor `4` is an implementation detail of `TokenThresholdPolicy`. If a
future impl uses a real tokenizer, it implements `ICompactionPolicy` directly —
no changes to callers.

---

### Summarizer — `ModelSessionSummarizer`

```csharp
public sealed class ModelSessionSummarizer(
    IChatClient chatClient,
    ISessionStore store,
    string modelId) : ISessionSummarizer
{
    const string SummarizerPrompt = """
        You are summarizing a coding agent session for context compaction.
        Produce a concise summary (under 400 tokens) covering:
        - The user's original goal
        - Key decisions made
        - Files read or written, with the action taken on each
        - The current state of the work and what remains

        Output plain prose. No headers. No lists. Start with "This session:".
        """;

    public async Task<string> SummarizeAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        IReadOnlyList<TurnRecord> turns = await store.GetTurnsAsync(sessionId, ct);

        string transcript = BuildTranscript(turns);

        IReadOnlyList<ChatMessage> messages =
        [
            new(ChatRole.User, $"{SummarizerPrompt}\n\n---\n\n{transcript}")
        ];

        ChatResponse response = await chatClient.GetResponseAsync(messages, ct);

        return response.Text ?? string.Empty;
    }

    static string BuildTranscript(IReadOnlyList<TurnRecord> turns)
    {
        System.Text.StringBuilder sb = new();

        foreach (TurnRecord turn in turns)
        {
            sb.AppendLine($"User: {turn.UserMessage}");
            sb.AppendLine($"Assistant: {turn.FinalAssistantMessage}");

            foreach (ToolCallRecord call in turn.ToolCalls)
                sb.AppendLine($"  [{call.Name}({call.ArgumentsJson}) → {call.Status}]");

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
```

`BuildTranscript` produces a compact, structured text representation of all turns.
Tool call results are omitted from the transcript — only name, arguments, and
status are included. Full results are queryable via `SearchSessionTool`. This
keeps the summarizer prompt well under the context limit even for long sessions.

---

### `SearchSessionTool`

```csharp
public sealed class SearchSessionTool(ISessionStore store) : ITool
{
    public string Name => "search_session";

    public string Description =>
        "Search the current session's tool call history for relevant output. " +
        "Use this after a context compaction to recall specific file contents, " +
        "command output, or search results from earlier in the session. " +
        "Returns up to 5 matching tool call records.";

    public IReadOnlyDictionary<string, ToolParameterDefinition> Parameters => ...;

    public async Task<string> ExecuteAsync(
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct = default)
    {
        string query = args["query"]?.ToString() ?? string.Empty;
        IReadOnlyList<ToolCallRecord> results =
            await store.SearchToolOutputsAsync(query, maxResults: 5, ct);
        return FormatResults(results);
    }
}
```

`SearchSessionTool` is registered in `ToolRegistry` alongside the existing tools.
It is always available to the model — not only after compaction — but the system
prompt and tool description guide the model to use it primarily for recall after a
compaction event.

---

### Program.cs — compaction check

The REPL loop gains one block between `await agentLoop.RunTurnAsync(...)` and the
next turn start. Pseudocode (actual types shown):

```csharp
// after each turn:
await sessionStore.AppendAsync(record);

if (compactionPolicy.ShouldCompact(conversationHistory))
{
    string summary = await summarizer.SummarizeAsync(SessionLogger.SessionId);
    conversationHistory =
    [
        new ChatMessage(ChatRole.System,
            $"[Session compacted]\n\n{summary}")
    ];
    Console.WriteLine("[context compacted — summary injected]");
}
```

`conversationHistory` is replaced, not mutated. The previous list is discarded.
The new list contains exactly one message — the summary — which becomes the base
for the next turn. The model receives this on its next call as part of the message
list, not as a system prompt change.

The `[context compacted]` line printed to the console matches the style of the
existing `[turn limit reached]` message.

---

### `AgentOptions` change

```csharp
public int CompactionThresholdTokens { get; init; } = 40_000;
```

One new property, default value chosen to be roughly half of a typical 80K context
window. `PhelixHost.Build()` reads this from config (or uses the default) and
passes it to `TokenThresholdPolicy`.

---

### Wiring in `PhelixHost`

```csharp
SqliteSessionStore sessionStore = new(SessionLogger.SessionId);
ICompactionPolicy compactionPolicy =
    new TokenThresholdPolicy(agentOptions.CompactionThresholdTokens);
ISessionSummarizer summarizer =
    new ModelSessionSummarizer(chatClient, sessionStore, activeModel.ModelId);

toolRegistry.Register(new SearchSessionTool(sessionStore));

return (agentLoop, sessionStore, compactionPolicy, summarizer, tracerProvider);
```

`PhelixHost.Build()` returns a struct or named tuple with the new components.
`Program.cs` receives them and drives the compaction check.

---

## Dependency

`Microsoft.Data.Sqlite` added to `Phelix.Core.csproj`. No other new dependencies.
The FTS5 extension ships with the SQLite amalgamation included in this package —
no separate native library needed.

---

## Contract

### `ISessionStore`
- `AppendAsync` is atomic: both `turns` and `tool_outputs` rows are written in a
  single transaction or neither is written.
- `GetTurnsAsync` returns turns in insertion order (ascending `StartedAt`).
- `SearchToolOutputsAsync` returns at most `maxResults` rows, ranked by FTS5
  relevance score.

### `ICompactionPolicy`
- `ShouldCompact` is pure — no side effects, no I/O.
- Returning `true` on an empty history is valid (the policy does not guard against
  this; the caller decides whether to act).

### `ISessionSummarizer`
- `SummarizeAsync` always returns a non-null string. On model failure it returns
  an empty string; the caller must handle the empty case.
- The returned string is plain text, not a `ChatMessage`. The caller wraps it.

### `SearchSessionTool`
- Returns a formatted string of at most 5 matching `ToolCallRecord` entries.
- On no results, returns a human-readable "no results found" string — never
  throws.

---

## Tests

### `SqliteSessionStore`
- `AppendAsync_WritesRowToTurnsTable`
- `AppendAsync_WritesRowsToToolOutputsTable_OnePerToolCall`
- `AppendAsync_IsAtomic_NeitherTableWrittenOnFailure`
- `GetTurnsAsync_ReturnsAllTurnsForSession_InInsertionOrder`
- `GetTurnsAsync_ExcludesTurnsFromOtherSessions`
- `SearchToolOutputsAsync_ReturnsMatchingRows`
- `SearchToolOutputsAsync_ReturnsAtMostMaxResults`
- `SearchToolOutputsAsync_ReturnsEmpty_WhenNoMatch`

All use an in-memory SQLite database (`:memory:`) — no file I/O in tests.

### `TokenThresholdPolicy`
- `ShouldCompact_ReturnsFalse_WhenBelowThreshold`
- `ShouldCompact_ReturnsTrue_WhenAtOrAboveThreshold`
- `ShouldCompact_ReturnsFalse_OnEmptyHistory`

### `ModelSessionSummarizer`
- `SummarizeAsync_ReturnsModelResponse_AsString`
- `SummarizeAsync_BuildsTranscriptFromAllTurns`
- `SummarizeAsync_ReturnsEmptyString_OnEmptyModelResponse`

Uses a fake `IChatClient` returning a canned string. Uses an in-memory
`SqliteSessionStore` seeded with known `TurnRecord` values.

### `SearchSessionTool`
- `ExecuteAsync_ReturnsFormattedResults_ForMatchingQuery`
- `ExecuteAsync_ReturnsNoResultsMessage_WhenQueryMatchesNothing`
- `ExecuteAsync_ReturnsAtMostFiveResults`

Uses an in-memory `SqliteSessionStore` seeded with known tool outputs.

### Integration — compaction trigger
- `ProgramLoop_CompactsHistory_WhenThresholdExceeded`

Uses a fake `IChatClient` that echoes canned responses and a
`TokenThresholdPolicy` initialized with threshold = 1 (fires immediately). Asserts
that after the second turn `conversationHistory.Count == 1` and the single message
content starts with `"[Session compacted]"`.

---

## Files

### New

```
src/Phelix.Core/Session/ISessionStore.cs
src/Phelix.Core/Session/SqliteSessionStore.cs
src/Phelix.Core/Session/ICompactionPolicy.cs
src/Phelix.Core/Session/TokenThresholdPolicy.cs
src/Phelix.Core/Session/ISessionSummarizer.cs
src/Phelix.Core/Session/ModelSessionSummarizer.cs
src/Phelix.Core/Tools/SearchSessionTool.cs
tests/Phelix.Core.Tests/Session/SqliteSessionStoreTests.cs
tests/Phelix.Core.Tests/Session/TokenThresholdPolicyTests.cs
tests/Phelix.Core.Tests/Session/ModelSessionSummarizerTests.cs
tests/Phelix.Core.Tests/Tools/SearchSessionToolTests.cs
tests/Phelix.Core.Tests/Integration/CompactionIntegrationTests.cs
```

### Modified

```
src/Phelix.Core/Agent/AgentOptions.cs      — add CompactionThresholdTokens
src/Phelix.Cli/PhelixHost.cs               — wire store, policy, summarizer, tool
src/Phelix.Cli/Program.cs                  — add compaction check in REPL loop
src/Phelix.Core/Phelix.Core.csproj        — add Microsoft.Data.Sqlite reference
```

### Unchanged

```
src/Phelix.Core/Agent/AgentLoop.cs         — no changes
src/Phelix.Core/Session/SessionLogger.cs   — no changes (JSONL write stays)
src/Phelix.Core/Session/TurnRecord.cs      — no changes
```

---

## Implementation order

1. Add `Microsoft.Data.Sqlite` to `Phelix.Core.csproj`
2. Define `ISessionStore` — interface only
3. Implement `SqliteSessionStore` with schema creation and `AppendAsync`
4. Implement `GetTurnsAsync` and `SearchToolOutputsAsync` on `SqliteSessionStore`
5. Write `SqliteSessionStoreTests` against `:memory:` — all passing before continuing
6. Define `ICompactionPolicy`; implement `TokenThresholdPolicy`
7. Write `TokenThresholdPolicyTests`
8. Define `ISessionSummarizer`; implement `ModelSessionSummarizer`
9. Write `ModelSessionSummarizerTests` with fake `IChatClient`
10. Implement `SearchSessionTool`
11. Write `SearchSessionToolTests`
12. Add `CompactionThresholdTokens` to `AgentOptions`
13. Update `PhelixHost.Build()` to wire all new components
14. Update `Program.cs` with the compaction check block
15. Write the integration test
