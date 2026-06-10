# Spec: Rich TUI — Rendering Layer

## Context

PR #24 (rich-tui foundation) extracted session orchestration from `Phelix.Cli/Program.cs`
into `PhelixSession` in `Phelix.Core`. It introduced `TurnCallbacks`, `TurnResult`, and
`TurnExitReason.Error`. The boundary is established. This phase builds the rendering
layer inside `Phelix.Tui` — the five remaining pieces listed in the roadmap.

## Problem

The CLI entry point works by blocking `Console.ReadLine()` on the main thread and
printing output directly to stdout. That model is incompatible with a live-updating
terminal display. A TUI must:

1. Own its keyboard loop independently of any blocking I/O
2. Receive agent events asynchronously (chunks, tool starts/completions, approval requests)
3. Update the display in response to both producers without race conditions
4. Never let a render call happen on two threads at the same time

## Decision

An unbounded `Channel<TuiEvent>` serializes all events onto a single consumer loop.
Two producers write to it: the agent callbacks (via `TurnCallbacks`) and the keyboard
input task. The consumer loop is the only code that reads from the channel, applies
events to `TuiState`, and calls `ctx.Refresh()`. No locks needed — serialization is
the invariant.

---

## Piece 1: `TuiEvent` hierarchy

File: `src/Phelix.Tui/TuiEvent.cs`

Seven event types as positional records under an abstract base:

```csharp
public abstract record TuiEvent;

public sealed record ChunkReceived(string Text) : TuiEvent;
public sealed record ToolStarted(string Name, IReadOnlyDictionary<string, object?> Args) : TuiEvent;
public sealed record ToolCompleted(string Name, ToolCallStatus Status, TimeSpan Duration) : TuiEvent;
public sealed record ApprovalRequested(
    string ToolName,
    string CallSummary,
    IReadOnlyDictionary<string, object?> Args,
    TaskCompletionSource<bool> Gate) : TuiEvent;
public sealed record TurnCompleted(TurnResult Result) : TuiEvent;
public sealed record TurnFailed(string ErrorMessage) : TuiEvent;
public sealed record KeyPressed(ConsoleKeyInfo Key) : TuiEvent;
```

`ApprovalRequested` carries `Args` separately from `CallSummary` so the renderer can
display structured argument rows (the key/value grid in the design) without parsing
the summary string. `Gate` is the `TaskCompletionSource<bool>` that the consumer loop
resolves when the user presses `y` or `n`.

---

## Piece 2: `TuiState` and `Apply`

File: `src/Phelix.Tui/TuiState.cs`

An immutable record holding every field the renderer needs to draw one frame:

```csharp
public sealed record TuiState(
    TuiPhase Phase,
    ImmutableArray<DisplayMessage> Messages,
    string CurrentInput,
    ToolCard? ActiveTool,
    int TotalTokens,
    ApprovalRequest? PendingApproval,
    string? ErrorMessage,
    int TurnNumber,
    int MaxTurns,
    string SessionId,
    string ModelId,
    string Provider);

public enum TuiPhase { Idle, Running, ToolRunning, AwaitingApproval, Error }
```

Supporting types:

```csharp
public sealed record DisplayMessage(
    string Speaker,         // "You" or "Phelix"
    string Text,
    DateTimeOffset Timestamp,
    ImmutableArray<ToolCard> ToolCards);

public sealed record ToolCard(
    string Name,
    IReadOnlyDictionary<string, object?> Args,
    ToolCardStatus Status,
    TimeSpan Duration);

public enum ToolCardStatus { Running, Done, Denied, Failed }

public sealed record ApprovalRequest(
    string ToolName,
    string CallSummary,
    IReadOnlyDictionary<string, object?> Args,
    TaskCompletionSource<bool> Gate);
```

`Apply` is a pure static function — no side effects, no I/O:

```csharp
public static TuiState Apply(TuiState current, TuiEvent e) => e switch
{
    ChunkReceived chunk       => /* append text to last Phelix message */
    ToolStarted started       => /* add running ToolCard, set phase ToolRunning */
    ToolCompleted completed   => /* flip ToolCard to Done/Denied/Failed, set phase Running */
    ApprovalRequested request => /* set phase AwaitingApproval, set PendingApproval */
    TurnCompleted turn        => /* set phase Idle or Error, clear ActiveTool */
    TurnFailed failed         => /* set phase Error, set ErrorMessage */
    KeyPressed key            => /* update CurrentInput or resolve PendingApproval */
    _                         => current
};
```

### Key Apply rules

**`ChunkReceived`:** If the last message in `Messages` is a Phelix message in the current
turn, append the text to it. Otherwise append a new `DisplayMessage` with an empty
`ToolCards` array. Phase moves to `Running` on the first chunk.

**`ToolStarted`:** Append a new `ToolCard` with `ToolCardStatus.Running` to the current
Phelix message's `ToolCards`. Set `ActiveTool` to that card. Phase becomes `ToolRunning`.

**`ToolCompleted`:** Find the `ToolCard` by name in `ActiveTool`, flip its status and
duration. Clear `ActiveTool`. Phase returns to `Running`.

**`ApprovalRequested`:** Set `PendingApproval` from the event's fields. Phase becomes
`AwaitingApproval`. The agent turn is blocked on `Gate.Task` — no state mutation needed
to pause it; it's already waiting.

**`TurnCompleted`:**
- On `TurnResult.Success` with `TurnExitReason.TurnLimitReached`: phase `Error`,
  `ErrorMessage` set to the turn-limit message.
- On `TurnResult.Success` normally: phase `Idle`, increment `TurnNumber`.
- `ActiveTool`, `PendingApproval`, `ErrorMessage` all cleared.

**`TurnFailed`:** Phase `Error`, `ErrorMessage` set to the event's message.

**`KeyPressed`:** Handled only in `Idle` and `AwaitingApproval` phases:
- `Idle`: printable characters append to `CurrentInput`; `Backspace` trims it;
  `Enter` is not handled here (handled directly in the keyboard loop to avoid
  a race between submitting and clearing the input).
- `AwaitingApproval`: `y` → `Gate.TrySetResult(true)`; `n`/`Escape` →
  `Gate.TrySetResult(false)`. After resolution, clear `PendingApproval`, phase `Running`.

---

## Piece 3: `TuiApprovalGate`

File: `src/Phelix.Tui/TuiApprovalGate.cs`

Implements `IApprovalGate`. Called by `AgentLoop` on every tool dispatch — on the
agent's background task, not on the consumer loop.

```csharp
public sealed class TuiApprovalGate(ChannelWriter<TuiEvent> eventWriter) : IApprovalGate
{
    public async Task<bool> RequestApprovalAsync(
        string toolName,
        ApprovalTier tier,
        string callSummary,
        CancellationToken cancellationToken)
    {
        if (tier == ApprovalTier.Auto)
            return true;

        TaskCompletionSource<bool> gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));

        await eventWriter.WriteAsync(
            new ApprovalRequested(toolName, callSummary, /* args */, gate),
            cancellationToken);

        return await gate.Task;
    }
}
```

`TaskCreationOptions.RunContinuationsAsynchronously` is non-negotiable. Without it,
`TrySetResult(true)` on the consumer loop thread would immediately execute the agent
turn's continuation — the next model call — inline on the consumer loop, freezing the
display until the model responds. The flag queues the continuation to the ThreadPool,
returning control to the consumer loop immediately.

The `CancellationToken.Register` call means a `Ctrl+C` during a pending approval
cancels `gate.Task` cleanly, which propagates cancellation up through `AgentLoop` and
back into `PhelixSession` without leaving the approval gate hanging.

`Args` needs to be threaded through from `AgentLoop` to `TuiApprovalGate`. The current
`IApprovalGate.RequestApprovalAsync` signature does not carry `args`. A new overload
or signature extension is needed:

```csharp
// IApprovalGate.cs — add args parameter
Task<bool> RequestApprovalAsync(
    string toolName,
    ApprovalTier tier,
    string callSummary,
    IReadOnlyDictionary<string, object?> args,
    CancellationToken cancellationToken);
```

`InteractiveApprovalGate` and `AutoApproveGate` both receive `args` and ignore them.
`AgentLoop` passes the already-resolved `args` dictionary it has at that point in the
dispatch loop.

---

## Piece 4: `TuiRenderer`

File: `src/Phelix.Tui/TuiRenderer.cs`

A pure static class. One public method:

```csharp
public static IRenderable Render(TuiState state)
```

Returns a Spectre.Console `Layout` or composed `Rows` that the live display refreshes.
No mutable fields. No side effects. Given the same `TuiState`, always returns the same
renderable.

### Layout structure

```
┌─ TopBar ──────────────────────────────────────────────┐
│ ◆ phelix  │  model · provider           turn N/M │ session XXXXX │
├───────────────────────────────────────────────────────┤
│                                                       │
│  [conversation history — scrollable]                  │
│  You ─────────────────────────────────                │
│  <user message>                                       │
│                                                       │
│  Phelix ───────────────────────────────               │
│  <tool cards>                                         │
│  <streaming text>                                     │
│                                                       │
│  [Spinner | ApprovalPanel | ErrorPanel | PromptInput] │
│                                                       │
├───────────────────────────────────────────────────────┤
│ BotBar: tokens  │  ◌ tool (if running)  │  keyhints  │
└───────────────────────────────────────────────────────┘
```

### Component rules (matching design tokens)

**TopBar:** `◆ phelix` in purple/bold · model name · dim provider · spacer ·
`turn N/M` in dim · session ID (short — first 5 chars of the GUID).

**BotBar:** token count (`N,NNN / 200k tok`) · active tool indicator (`◌ toolname`)
only when `phase == ToolRunning` · keybinding hints right-aligned.
Keyhints vary by phase:
- `Idle`: `q quit  ? help  ctrl+c cancel`
- `Running` / `ToolRunning`: `ctrl+c cancel  ? help`
- `AwaitingApproval`: `y approve  n deny  esc cancel`
- `Error`: `r retry  q quit  ? help`

**Conversation area:** iterate `Messages`. Each message:
- Divider rule: speaker name + dim horizontal rule + timestamp (relative: "now", "3m ago")
- User messages: plain text, dim color
- Phelix messages: tool cards first, then text

**Tool cards** (collapsed for all but the most recent Phelix message):
- Header: status icon + tool name + spacer + status label/duration
  - `◌` orange for running, `✓` green for done, `✗` red for failed, `—` dim for denied
- Args: key/value rows — only shown on the active (non-collapsed) card
- Border: `C.border` normally; orange for running

**Spinner:** shown when `Phase == Running` and no chunks have arrived yet in the
current turn segment — i.e., between a `ToolCompleted` and the next `ChunkReceived`.
`◌ thinking ···` in muted color.

**PromptInput:** shown only when `Phase == Idle`.
`›` purple/bold · current input text (or dim placeholder) · blinking cursor block.
Spectre does not natively blink — render a `█` in purple; full blink is a nice-to-have.

**ApprovalPanel:** shown when `Phase == AwaitingApproval`. Orange border.
Header: `⚠ Tool Approval Required`. Body: tool/args grid. Footer: key legend.

**ErrorPanel:** shown when `Phase == Error`. Red border.
Header: `✗ <error title>`. Body: error message + hint. Footer: `r retry  q quit`.

### Spectre.Console notes

- Use `Markup.Escape()` on all user-controlled strings before embedding in markup.
- Token counts: format with `N` format specifier (`4231.ToString("N0")` → `"4,231"`).
- Relative timestamps: compute from `DisplayMessage.Timestamp` to `DateTimeOffset.UtcNow`
  at render time. `< 60s` → `"now"`, `< 60m` → `"Nm ago"`, else `"Nh ago"`.
- The live display area is `AnsiConsole.Live(new Text(""))` initialized before the loop,
  updated via `ctx.UpdateTarget(renderer.Render(state))` then `ctx.Refresh()`.

---

## Piece 5: `TuiSession`

File: `src/Phelix.Tui/TuiSession.cs`

The entry point for the TUI. Equivalent of `Program.cs` but event-driven.

```csharp
public sealed class TuiSession(PhelixSession session)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
}
```

### Startup

1. Initialize `TuiState` from `session` (model, session ID, max turns, etc.)
2. Create `Channel<TuiEvent>.CreateUnbounded<TuiEvent>()`
3. Start keyboard input task (background, loops until cancellation)
4. Start consumer loop task
5. `await Task.WhenAll(keyboardTask, consumerTask)`

### Keyboard input task

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    ConsoleKeyInfo key = Console.ReadKey(intercept: true);
    await channel.Writer.WriteAsync(new KeyPressed(key), cancellationToken);
}
```

`intercept: true` suppresses the default terminal echo — the renderer draws the input
line from `TuiState.CurrentInput` instead.

`Enter` is special — handled in the consumer loop (not here) because submitting the
prompt needs to interact with `PhelixSession` and clear the input atomically.

### Consumer loop

```csharp
TuiState state = initialState;

await AnsiConsole.Live(TuiRenderer.Render(state))
    .StartAsync(async ctx =>
    {
        await foreach (TuiEvent e in channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (e is KeyPressed { Key.Key: ConsoleKey.Enter } && state.Phase == TuiPhase.Idle)
            {
                string prompt = state.CurrentInput.Trim();
                if (string.IsNullOrEmpty(prompt)) continue;

                state = state with { CurrentInput = string.Empty, Phase = TuiPhase.Running };
                ctx.Refresh();

                // Fire and forget onto ThreadPool — agent runs concurrently with consumer loop
                _ = Task.Run(async () =>
                {
                    TurnCallbacks callbacks = BuildCallbacks(channel.Writer);
                    TurnResult result = await session.RunTurnAsync(prompt, callbacks, cancellationToken);
                    await channel.Writer.WriteAsync(new TurnCompleted(result), cancellationToken);
                }, cancellationToken);

                continue;
            }

            state = TuiState.Apply(state, e);
            ctx.UpdateTarget(TuiRenderer.Render(state));
            ctx.Refresh();
        }
    });
```

`Task.Run` is deliberate. `PhelixSession.RunTurnAsync` blocks for the full duration of
the model call. Running it inline would stall the consumer loop — no keyboard events
would process, no display updates would occur. `Task.Run` moves it to the ThreadPool;
the consumer loop returns immediately to reading the channel.

The `TurnCompleted` event is written by the background task when the turn finishes.
This is the signal that brings the consumer back to `Idle` phase and re-enables input.

### `BuildCallbacks`

```csharp
static TurnCallbacks BuildCallbacks(ChannelWriter<TuiEvent> writer) => new(
    OnChunk: text => writer.WriteAsync(new ChunkReceived(text)).AsTask(),
    OnToolStarted: (name, args) => writer.WriteAsync(new ToolStarted(name, args)).AsTask(),
    OnToolCompleted: (name, status, dur) => writer.WriteAsync(new ToolCompleted(name, status, dur)).AsTask()
);
```

---

## `IApprovalGate` signature change

The existing signature:

```csharp
Task<bool> RequestApprovalAsync(
    string toolName,
    ApprovalTier tier,
    string callSummary,
    CancellationToken cancellationToken);
```

New signature — adds `args`:

```csharp
Task<bool> RequestApprovalAsync(
    string toolName,
    ApprovalTier tier,
    string callSummary,
    IReadOnlyDictionary<string, object?> args,
    CancellationToken cancellationToken);
```

All three implementations updated:
- `AutoApproveGate` — ignores `args`, returns `true`
- `InteractiveApprovalGate` — ignores `args` (does not display them; the terminal
  gate is text-only)
- `TuiApprovalGate` — uses `args` to populate the `ApprovalRequested` event

`AgentLoop` passes the already-resolved `args` dictionary at the dispatch site.

---

## `q` to quit

`TuiSession` handles `q` as a quit key in `Idle` phase. This completes the channel
by calling `channel.Writer.Complete()`, which causes `ReadAllAsync` to drain and
return, ending the consumer loop.

---

## Files produced

**`Phelix.Core`** (interface change only)
- `Agent/IApprovalGate.cs` — add `args` parameter to `RequestApprovalAsync`
- `Agent/AgentLoop.cs` — pass `args` to `RequestApprovalAsync`
- `Agent/AutoApproveGate.cs` — accept and ignore `args`
- `Agent/InteractiveApprovalGate.cs` — accept and ignore `args`

**`Phelix.Tui`** (all new except `TerminalRenderer.cs`)
- `TuiEvent.cs` — event record hierarchy
- `TuiState.cs` — immutable state record + `Apply` function
- `TuiApprovalGate.cs` — `IApprovalGate` via `TaskCompletionSource<bool>`
- `TuiRenderer.cs` — `TuiState → IRenderable` pure function
- `TuiSession.cs` — keyboard loop, channel, consumer loop, Spectre live display
- `TerminalRenderer.cs` — kept unchanged; used by `Phelix.Cli` only

**`Phelix.Core.Tests`**
- `Agent/TuiApprovalGateTests.cs` — verify gate resolves correctly on y/n/cancel
- `TuiStateApplyTests.cs` — verify every event type produces the correct next state

---

## What is deferred

- **Scroll / paging** — history longer than the terminal height is truncated from the
  top for now. Full scroll support requires tracking a scroll offset in `TuiState` and
  mapping keyboard arrows to scroll events.
- **`d` (diff) key in approval gate** — the design shows a diff view in the approval
  panel. `write_file` does not currently produce a diff. Deferred until `WriteFileTool`
  returns structured before/after content.
- **Relative timestamp polling** — timestamps like "3m ago" drift while the display is
  idle. A ticker event could re-render on an interval. Deferred — timestamps are
  accurate at the moment they're rendered.
- **`a` (always approve) key** — requires flipping the session's `SessionMode` at
  runtime and rebuilding the approval gate mid-session. Deferred to a later gate
  enhancement.
- **`ctrl+r` (new session)** — requires constructing a fresh `PhelixSession`. Deferred.
