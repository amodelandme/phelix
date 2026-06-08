# Implementation Notes: Rich TUI

## What was built in this phase

This phase is the first half of the Rich TUI milestone — the foundation work that
makes a TUI possible without duplicating session logic. No terminal rendering was
added. What changed is where the harness lives.

Five new types. Two files rewritten. One enum value added.

## `TurnCallbacks` — callbacks are per-turn, not per-session

`AgentOptions` is session configuration: model ID, system prompt, max turns, approval
gate. These are fixed for the lifetime of a session.

Callbacks are different. The CLI needs `OnChunk` to print streaming text. The TUI
needs all three callbacks to drive live rendering. A headless test might need none.
The right place for them is at the call site — passed into `RunTurnAsync`, not stored
on the session.

`TurnCallbacks` is a `readonly record struct`: value semantics, stack-allocated, no
heap pressure on construction. All three delegates are nullable `Func<…, Task>?`, so
callers that don't need a callback pay nothing — no allocation, no virtual dispatch.

The invocation pattern throughout `AgentLoop`:

```csharp
if (callbacks.OnChunk is { } onChunk && !string.IsNullOrEmpty(update.Text))
    await onChunk(update.Text);
```

The `is { }` null check captures the delegate into a local before the await. This is
the correct pattern: it avoids a null-conditional delegate invocation and makes the
intent readable.

## `AgentLoop` tool callback placement

`OnToolStarted` and `OnToolCompleted` fire in three branches of the tool dispatch:

- **Approved and executed:** `OnToolStarted` fires before `ExecuteAsync`; a `Stopwatch`
  runs during execution; `OnToolCompleted` fires with `ToolCallStatus.Succeeded` and
  the measured elapsed time.
- **Denied:** both fire immediately with `ToolCallStatus.Denied` and `TimeSpan.Zero`.
  The TUI needs to know the tool was considered and rejected — not just silently skipped.
- **Unknown tool (not registered):** same as denied — both fire with
  `ToolCallStatus.Failed` and `TimeSpan.Zero`. An empty `Dictionary<string, object?>`
  is passed as args since there are none to report.

The denied branch fires both callbacks even though no execution occurred. This is
intentional: the TUI renders a tool card for every tool the model requested. A denied
card and a failed card need to appear just as a succeeded one does.

## `TurnExitReason.Error`

Added so the session log has a distinct exit reason for failed turns. Without it,
every failure would need to be inferred from the absence of a log entry — fragile and
unobservable. `Error` is never set by `AgentLoop` itself; it is set by `PhelixSession`
when it catches an exception and writes the failure record.

## `TurnResult` — no throwing across the session boundary

`PhelixSession.RunTurnAsync` returns `TurnResult` and never throws. The two cases:

- `TurnResult.Success(Turn turn)` — normal exit or turn-limit halt. The caller reads
  `turn.ExitReason` to distinguish them.
- `TurnResult.Failure(string errorMessage)` — any unhandled exception. The message is
  surfaced; the stack trace is not.

Entry points (CLI, future TUI) pattern-match on the result:

```csharp
switch (result)
{
    case TurnResult.Success success when success.Turn.ExitReason == TurnExitReason.TurnLimitReached:
        Console.WriteLine("[turn limit reached]");
        break;
    case TurnResult.Failure failure:
        Console.WriteLine($"Error: {failure.ErrorMessage}");
        break;
}
```

This keeps all error-display logic in the entry point, where it belongs. `PhelixSession`
never touches the terminal.

## `PhelixSession` — what moved, what changed

Everything that was in `Program.cs` between `RunTurnAsync` and the next `Console.Write`
prompt is now inside `PhelixSession.RunTurnAsync`:

- Building the turn ID and start timestamp
- Calling `AgentLoop.RunTurnAsync`
- Constructing `TurnRecord.FromTurn` and writing to both `SessionLogger` and
  `ISessionStore`
- Checking `ICompactionPolicy.ShouldCompact` and replacing history with the summary

On failure, `PhelixSession` catches the exception, writes a minimal `TurnRecord` with
`TurnExitReason.Error` and empty fields to both log sinks, and returns
`TurnResult.Failure`. Conversation history is not updated — the session is still in
the last known good state.

The logging of a failed turn is best-effort: if the log write itself throws, the
exception is swallowed. The original exception's message is what gets returned to the
caller. Logging failure must not mask the turn failure.

## `TotalTokenCount` on `PhelixSession`

Added as a convenience for the TUI's bot bar token display. `PhelixSession` accumulates
`InputTokens + OutputTokens` from each successful turn's `UsageSummary`. Failed turns
contribute zero (they have no usage to report). The CLI ignores this property today.

## `PhelixHost` — simplified return tuple

The return tuple shrank from five elements to three:

```
Before: (AgentLoop, ISessionStore, ICompactionPolicy, ISessionSummarizer, TracerProvider?)
After:  (PhelixSession, ISessionStore, TracerProvider?)
```

`ICompactionPolicy` and `ISessionSummarizer` are now internal to `PhelixSession`.
Entry points never needed them directly — they only existed in the tuple because
`Program.cs` called compaction and summarization manually.

## `Program.cs` after the extraction

What remains in `Program.cs`:

1. Parse `--allow-all` / `--accepts-edits` from `args`
2. Call `PhelixHost.Build(sessionMode)`
3. REPL loop: read input, call `session.RunTurnAsync`, pattern-match the result,
   print as appropriate

The compaction notice (`[context compacted — summary injected]`) that appeared in the
old `Program.cs` is gone. `PhelixSession` performs compaction silently. Entry points
will surface it when they have a better signal for it — the TUI will show it inline
in the conversation history.

## What comes next

This phase establishes the boundary. The next phase builds inside `Phelix.Tui`:

- `TuiApprovalGate` — `IApprovalGate` implementation using
  `TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)`,
  registered against the turn's `CancellationToken`
- `TuiEvent` hierarchy — events that flow through the channel from both the agent
  callbacks and the keyboard loop
- `TuiState` — immutable record; `Apply(TuiState, TuiEvent)` pure function
- `TuiRenderer` — `TuiState → IRenderable`; no mutable state
- `TuiSession` — keyboard loop, channel, consumer loop, Spectre.Console live display
