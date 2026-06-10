# TUI Entry Point Wiring — Spec

## Problem

`Phelix.Tui` contains all five rendering-layer pieces (`TuiEvent`, `TuiState`,
`TuiApprovalGate`, `TuiRenderer`, `TuiSession`) but has no entry point. The TUI
is unreachable. Users can only interact with the agent through the synchronous
`Console.ReadLine` REPL in `Phelix.Cli/Program.cs`.

## Decision

**TUI is the default.** `phelix` with no flags starts the TUI. The terminal REPL
is the opt-in path, enabled by `--cli`.

**Single binary, single composition root.** `Phelix.Cli` remains the only
executable. `Phelix.Tui` stays a class library. All NuGet infrastructure
dependencies (OpenAI, OTel, System.CommandLine) stay in `Phelix.Cli`. No
composition logic is duplicated.

## Invocation modes

| Command | Behaviour |
|---|---|
| `phelix` | Starts TUI. |
| `phelix --cli` | Starts terminal REPL. Waits for input, then loops. |
| `phelix --cli "prompt"` | Runs one turn in the terminal, exits. |
| `phelix --cli --accepts-edits` | REPL with reduced friction (Prompt-tier auto-approved). |
| `phelix --cli --allow-all` | REPL with all tools auto-approved. Prints warning. |

`--accepts-edits` and `--allow-all` are CLI-only. In TUI mode approval is
handled interactively through the approval panel — `SessionMode` is always
`Default`.

## Channel ownership and the sequencing problem

`TuiApprovalGate` needs a `ChannelWriter<TuiEvent>` at construction time.
`TuiSession` owns the `Channel<TuiEvent>` at runtime. These two facts create
a sequencing tension: the gate must exist before `PhelixSession` is built, but
the channel is an internal detail of session execution.

**Resolution:** the channel is created by `Program.cs` and passed into both
sides explicitly.

```
Program.cs
  │
  ├── Channel<TuiEvent> channel = Channel.CreateUnbounded(...)
  │
  ├── PhelixHost.Build(new HostMode.Tui(channel.Writer))
  │     └── TuiApprovalGate(channel.Writer)  ← gate wired here
  │         returns PhelixSession
  │
  └── new TuiSession(session, initialState, channel)
        └── RunAsync()  ← channel consumed here
```

The channel's lifetime is scoped to `Program.cs` — it is created before `Build`
and lives until `RunAsync` returns. Ownership is explicit and visible at the
call site.

## HostMode discriminated union

`PhelixHost.Build` currently accepts a `SessionMode` enum. This is replaced by a
`HostMode` discriminated union that carries exactly what differs between modes:

```csharp
internal abstract record HostMode
{
    internal sealed record Cli(SessionMode SessionMode) : HostMode;
    internal sealed record Tui(ChannelWriter<TuiEvent> EventWriter) : HostMode;
}
```

`BuildApprovalGate` switches on `HostMode` once. No conditionals elsewhere.
`Build`'s signature becomes `Build(HostMode mode)` with `HostMode.Tui` as the
default (matching the default invocation).

## TuiState construction

`TuiState` requires `ModelId`, `Provider`, `MaxTurns`, and `SessionId` at
construction — metadata that `PhelixHost.Build` already resolves from config.
`Build` returns this alongside `PhelixSession` so `Program.cs` can construct
`TuiState` without re-reading config.

The return type of `Build` grows a `TuiState? InitialState` field. It is
non-null when `mode` is `HostMode.Tui`, null for `HostMode.Cli`.

## System.CommandLine wiring

The current `Program.cs` uses manual `args.Contains(...)` checks. This is
replaced with a proper `System.CommandLine` root command:

- Root command (no subcommand) → TUI path
- `--cli` option → CLI path
- `--cli` accepts an optional positional `prompt` argument
- `--accepts-edits` and `--allow-all` are options on `--cli` (or globally
  declared but only meaningful with `--cli`)

## Files changed

| File | Change |
|---|---|
| `Phelix.Cli/Program.cs` | Replaced with System.CommandLine routing; TUI default |
| `Phelix.Cli/PhelixHost.cs` | `SessionMode` param replaced with `HostMode`; returns `InitialState` |
| `Phelix.Cli/HostMode.cs` | New: discriminated union |
| `Phelix.Tui/TuiSession.cs` | Accept `Channel<TuiEvent>` via constructor instead of creating internally |

## What is not changing

- `Phelix.Tui.csproj` — stays a class library, no `OutputType` change, no new packages
- `Phelix.Core` — untouched
- All existing tests — no changes required
- `TuiApprovalGate`, `TuiRenderer`, `TuiState`, `TuiEvent` — untouched
