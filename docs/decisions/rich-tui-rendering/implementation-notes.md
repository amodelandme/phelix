# Implementation Notes: Rich TUI — Rendering Layer

## What was built in this phase

The five pieces listed in the roadmap under "remaining TUI work":
`TuiEvent`, `TuiState`, `TuiApprovalGate`, `TuiRenderer`, `TuiSession`.

Plus a prerequisite interface change: `IApprovalGate.RequestApprovalAsync` gains an
`args` parameter so `TuiApprovalGate` can pass structured arguments into the approval
panel event.

The TUI is fully wired internally. One step remains before it is runnable: connecting
`TuiSession` to an entry point. See the roadmap for the two options.

---

## `IApprovalGate` — the args parameter

The existing signature had no way to pass tool arguments to the gate. `InteractiveApprovalGate`
only ever showed a text summary. `TuiApprovalGate` needs the full argument dictionary to
render the key/value grid in the approval panel.

Adding `args` to the interface is the right place — it belongs alongside `toolName` and
`callSummary` as data the gate uses to present the call to the user. `AutoApproveGate` and
`InteractiveApprovalGate` accept it and ignore it. No behaviour change for existing gates.

`AgentLoop` passes the already-resolved `args` dictionary it holds at the dispatch site —
no new work needed there.

---

## `TuiEvent` — why abstract record, not interface

`TuiEvent` could have been an interface. An abstract record was chosen because:

- Pattern matching on abstract records is exhaustive — the compiler warns on unhandled cases
- Records carry no behaviour, only data — which is exactly what events are
- The `sealed` keyword on each case prevents accidental subclassing

The seven event types map directly to the seven moments the TUI cares about:
agent streaming, tool start/end, approval gate, turn completion, turn failure, and keypress.

---

## `TuiState.Apply` — Enter is not handled here

The `KeyPressed` case in `Apply` handles printable characters and `Backspace` in `Idle`
phase, and `y`/`n`/`Escape` in `AwaitingApproval` phase. It does not handle `Enter`.

This is intentional. Submitting a prompt has side effects — it calls `PhelixSession.RunTurnAsync`
via `Task.Run`. A pure function cannot do that. `Enter` is handled directly in the consumer
loop of `TuiSession`, which is the only place with access to both `PhelixSession` and the
channel writer.

---

## `TuiApprovalGate` — `RunContinuationsAsynchronously`

`TaskCreationOptions.RunContinuationsAsynchronously` on the `TaskCompletionSource<bool>`
is the critical correctness flag. Without it:

1. User presses `y` in the consumer loop
2. `TrySetResult(true)` executes inline on the consumer loop thread
3. The agent turn's continuation runs — a full model call — synchronously on that thread
4. The consumer loop is frozen for the entire model call duration
5. No keyboard events process, no display updates occur

With the flag, `TrySetResult(true)` queues the continuation to the ThreadPool and returns
immediately. The consumer loop is free after a single event.

The `CancellationToken.Register` callback calls `TrySetCanceled` on the TCS. This ensures
a `Ctrl+C` during a pending approval unblocks the gate and propagates cancellation cleanly
up through `AgentLoop` — no hanging `await gate.Task`.

---

## `TuiRenderer` — pure function, `Color.FromHex`

`TuiRenderer.Render` is a pure static function. No fields, no side effects. The consumer
loop calls it after every state transition and passes the result to `ctx.UpdateTarget`.

Spectre.Console 0.55.2 uses `Color.FromHex(string)` for hex colour parsing — not
`Color.Parse`. All design-token colours in the renderer use hex strings matching the
HTML mockup palette, resolved via `Color.FromHex` at render time.

All user-controlled strings (model output, tool names, file paths, arguments) are passed
through `Markup.Escape` before embedding in markup. This prevents Spectre.Console markup
injection — a class of display corruption where a tool name or file path containing `[`
brackets would be interpreted as Spectre markup.

---

## `TuiSession` — `Task.Run` for agent turns

`PhelixSession.RunTurnAsync` blocks for the full duration of a model call — potentially
many seconds. Running it inline on the consumer loop would freeze the display entirely.

`Task.Run` moves it to the ThreadPool. The consumer loop returns immediately to reading
the channel. The agent posts `TurnCompleted` when it finishes, which the consumer picks
up and applies to state like any other event.

`SingleReader = true` on the `UnboundedChannelOptions` is a minor throughput hint —
the channel can skip locking on the read side since there is exactly one consumer.

---

## What comes next

Wire `TuiSession` into an entry point. Two options:

**Option 1 — `--tui` flag on `Phelix.Cli`**
`PhelixHost.Build` detects the flag and:
- Constructs a `TuiApprovalGate` (instead of `InteractiveApprovalGate`) wired to the
  channel writer
- Returns a `TuiState` alongside `PhelixSession` (model ID, provider, session ID, max turns)
- `Program.cs` branches: `--tui` → `new TuiSession(session, initialState).RunAsync()`

**Option 2 — separate `Phelix.Tui` executable**
A `Program.cs` in `Phelix.Tui` that calls `PhelixHost.Build` and drives `TuiSession.RunAsync`.
Keeps `Phelix.Cli` entirely unchanged. Cleaner long-term separation.

The spec recommends Option 1 for now — one binary, one install, toggle via flag.
