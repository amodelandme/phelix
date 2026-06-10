# Spec: Rich TUI

## Problem

The CLI entry point (`Phelix.Cli`) owns the entire session lifecycle: it calls
`AgentLoop.RunTurnAsync`, checks the compaction policy, logs the turn, and manages
conversation history — all inside `Program.cs`. A second entry point (the TUI) would
have to duplicate every one of those responsibilities to function correctly. That
duplication is the root problem, not the rendering.

Separately, the CLI's input model — `Console.ReadLine()` blocking the main thread —
cannot coexist with a live-updating terminal display. A proper TUI needs to own its
own keyboard loop and render in response to async events from the running agent.

## Decision

Two changes, in sequence:

**1. Extract `PhelixSession` into `Phelix.Core`.**
All session orchestration logic currently in `Program.cs` moves to a new
`PhelixSession` class in `Phelix.Core`: maintaining conversation history, calling
`AgentLoop.RunTurnAsync`, checking the compaction policy, and logging the turn. Both
`Phelix.Cli` and `Phelix.Tui` construct and drive a `PhelixSession`; neither
re-implements the session rules.

**2. Build `TuiSession` in `Phelix.Tui` as a separate entry point.**
`TuiSession` owns the keyboard input loop, the channel that serializes all events onto
one consumer, and the Spectre.Console live display. It drives `PhelixSession` the same
way `Program.cs` drives it today — one call per user prompt — but receives events via
callbacks rather than printing directly to stdout.

`Phelix.Cli` is unchanged except for the extraction of session logic into
`PhelixSession`.

## Callbacks and `TurnCallbacks`

`AgentLoop.RunTurnAsync` currently accepts one callback: `Func<string, Task>? onChunk`.
Two more are needed so the TUI can render tool cards:

- `OnChunk(string text)` — fires for each streamed text fragment on the final response
- `OnToolStarted(string toolName, IReadOnlyDictionary<string, object?> args)` — fires
  immediately before a tool executes
- `OnToolCompleted(string toolName, ToolCallStatus status, TimeSpan duration)` — fires
  immediately after a tool completes (succeeded, denied, or failed)

These are grouped into a `TurnCallbacks` readonly record struct and passed to
`RunTurnAsync` as a single parameter, replacing the bare `onChunk` parameter. All
three are `Func<…, Task>?` — nullable async delegates — so callers that don't need a
callback omit it without allocating anything.

`TurnCallbacks` is a per-turn concern. It does not belong on `AgentOptions`, which
holds session-level configuration (model ID, system prompt, max turns, compaction
threshold, approval gate).

## `PhelixSession`

Owns one conversation's lifetime. Responsibilities:

- Holds `IReadOnlyList<ChatMessage> conversationHistory`
- Calls `AgentLoop.RunTurnAsync` with the provided `TurnCallbacks`
- On success: appends the turn to the session log and SQLite store, checks compaction
  and replaces history with the summary when triggered
- On failure: logs the failed turn with `TurnExitReason.Error` (new enum value),
  leaves `conversationHistory` unchanged (no partial forward progress on a broken turn)
- Returns a `TurnResult` (success or failure, with the exit reason) so callers can
  decide what to display

`PhelixSession` is not static. It is constructed once per session via `PhelixHost` and
passed to the entry point (CLI or TUI).

## `TuiSession` and the event model

`TuiSession` is the TUI's equivalent of `Program.cs`. It:

1. Constructs a `TurnCallbacks` whose delegates write `TuiEvent` records to an
   unbounded `Channel<TuiEvent>`
2. Starts a keyboard input task that reads `Console.ReadKey(intercept: true)` and
   posts `TuiEvent` records for each keypress
3. Runs a consumer loop that reads from the channel, applies events to `TuiState`, and
   calls `ctx.Refresh()` on the Spectre.Console live display
4. On `Enter`, submits the buffered input to `PhelixSession.RunTurnAsync` as a
   background `Task`

All events from both producers (callbacks from the running agent, keypresses from the
user) flow through the same channel. The consumer loop is the only place that touches
`TuiState` or calls Spectre — no cross-thread rendering.

### `TuiState`

An immutable record holding everything the renderer needs:

```
Phase          — Idle | Running | ToolRunning | AwaitingApproval | Error
Messages       — ImmutableArray<DisplayMessage>
CurrentInput   — string (the user's in-progress prompt)
ActiveTool     — string? (non-null only in ToolRunning)
TokenCount     — int
PendingApproval — ApprovalRequest? (non-null only in AwaitingApproval)
```

Each event produces a new `TuiState` via a pure `Apply(TuiState current, TuiEvent e)`
function. Immutability means the renderer can read state without a lock; the channel
serializes all writes.

### `TuiEvent` hierarchy

```
ChunkReceived(string Text)
ToolStarted(string Name, IReadOnlyDictionary<string, object?> Args)
ToolCompleted(string Name, ToolCallStatus Status, TimeSpan Duration)
ApprovalRequested(string ToolName, string CallSummary, TaskCompletionSource<bool> Gate)
TurnCompleted(TurnResult Result)
TurnFailed(string ErrorMessage)
KeyPressed(ConsoleKeyInfo Key)
```

All are positional records. `ApprovalRequested` carries the `TaskCompletionSource<bool>`
so the consumer loop can resolve it when the user presses `y` or `n`.

## `TuiApprovalGate`

Implements `IApprovalGate`. When `RequestApprovalAsync` is called:

1. Creates a `TaskCompletionSource<bool>` with
   `TaskCreationOptions.RunContinuationsAsynchronously`
2. Registers a callback on the `CancellationToken` that calls `TrySetCanceled()` —
   so a `ctrl+c` during a pending approval unblocks the gate and propagates cancellation
   up into `AgentLoop` immediately
3. Writes an `ApprovalRequested` event to the channel
4. Awaits `tcs.Task` and returns the result

The keyboard loop, when in `AwaitingApproval` phase, resolves the pending
`TaskCompletionSource<bool>` directly: `y` → `TrySetResult(true)`,
`n` → `TrySetResult(false)`, `Esc` → `TrySetResult(false)`.

The `TaskCreationOptions.RunContinuationsAsynchronously` flag ensures the thread
that presses `y` (the consumer loop) does not immediately execute the rest of the
agent turn inline — the continuation is queued back onto the ThreadPool, keeping
the consumer loop free.

## Rendering

`TuiRenderer` in `Phelix.Tui` translates `TuiState` into a Spectre.Console
renderable tree. It is a pure function: given a `TuiState`, produce a `Layout` or
`Panel` composition. It has no mutable state of its own.

The Spectre.Console `Live` display wraps the entire content area. `ctx.Refresh()` is
called by the consumer loop after every state transition.

Key rendering rules that mirror the design mockups:

- `TopBar`: model · provider · turn N/max · session ID — always visible
- `BotBar`: token count · active tool indicator (only during `ToolRunning`) ·
  keybinding hints — always visible
- Tool cards: shown as `◌ running` during `ToolRunning`, flipped to `✓ done` (with
  duration) on `ToolCompleted`. Previous turns' tool cards are collapsed (header only).
- Spinner: shown between `ToolCompleted` and the next `ChunkReceived` — i.e., while
  the model is thinking after getting tool results back
- Approval gate: replaces the prompt input during `AwaitingApproval`; shows tool name,
  arguments, diff if applicable, and the `y / n / d / a / Esc` key legend
- Error panel: shown on `TurnFailed` and on `TurnExitReason.TurnLimitReached`

## New `TurnExitReason` value

`TurnExitReason.Error` is added to record turns that ended due to an unhandled
exception. `PhelixSession` catches, logs the record with this reason, and surfaces the
error message to the caller.

## Files changed

**`Phelix.Core`**
- `Agent/TurnCallbacks.cs` — new; `readonly record struct` with three nullable async delegates
- `Agent/AgentLoop.cs` — replace `onChunk` parameter with `TurnCallbacks`; add `OnToolStarted`/`OnToolCompleted` invocations in the tool dispatch loop with timing via `Stopwatch`
- `Session/TurnExitReason.cs` — add `Error` value
- `Session/TurnResult.cs` — new; discriminated union (success / failure) returned by `PhelixSession.RunTurnAsync`
- `PhelixSession.cs` — new; session orchestration extracted from `Phelix.Cli/Program.cs`

**`Phelix.Cli`**
- `Program.cs` — replace inline orchestration with `PhelixSession`; adapt `TerminalRenderer.WriteChunk` to `TurnCallbacks`
- `PhelixHost.cs` — construct and return `PhelixSession`

**`Phelix.Tui`**
- `TuiApprovalGate.cs` — new; `IApprovalGate` implementation using `TaskCompletionSource<bool>`
- `TuiEvent.cs` — new; event record hierarchy
- `TuiState.cs` — new; immutable state record and `Apply` function
- `TuiRenderer.cs` — new; pure `TuiState → IRenderable` function (replaces the stub `TerminalRenderer.cs`)
- `TuiSession.cs` — new; keyboard loop, channel, consumer loop, Spectre live display
- `TerminalRenderer.cs` — remove or repurpose once `TuiSession` is functional

**`tests/Phelix.Core.Tests`**
- `Agent/AgentLoopTests.cs` — update call sites for `TurnCallbacks`; add tests verifying `OnToolStarted`/`OnToolCompleted` fire with correct arguments
- `Session/PhelixSessionTests.cs` — new; verify compaction triggers, failure logging, history unchanged on error
