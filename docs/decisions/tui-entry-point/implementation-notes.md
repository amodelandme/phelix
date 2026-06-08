# TUI Entry Point Wiring — Implementation Notes

## What was built

Four files changed:

**`Phelix.Cli/HostMode.cs`** (new) — discriminated union with two cases:
- `HostMode.Tui(ChannelWriter<TuiEvent> EventWriter)` — default path
- `HostMode.Cli(SessionMode SessionMode)` — opt-in terminal path

**`Phelix.Cli/PhelixHost.cs`** — `SessionMode` parameter replaced with `HostMode`.
`BuildApprovalGate` now switches on `HostMode` in one place: `Tui` → `TuiApprovalGate`,
`Cli/AllowAll` → `AutoApproveGate`, everything else → `InteractiveApprovalGate`.
Return type extended with `TuiState? InitialState` (non-null for `HostMode.Tui`).
`TuiState` is constructed here from config metadata (`ModelId`, `Provider`, `MaxTurns`,
`SessionLogger.SessionId`) — the only place that already has all of it.

**`Phelix.Tui/TuiSession.cs`** — constructor gains a `Channel<TuiEvent> channel` parameter.
Previously the session created the channel internally. Now the channel is created by
`Program.cs` before `PhelixHost.Build` and passed to both `TuiApprovalGate` (via
`HostMode.Tui`) and `TuiSession`. This resolves the sequencing problem: the gate and
session share the same writer without either needing to know about the other.

**`Phelix.Cli/Program.cs`** — rewritten with the preview-4 `System.CommandLine` API.
Key API differences from the stable version:
- `command.Options.Add(option)` / `command.Arguments.Add(arg)` (not `AddOption`/`AddArgument`)
- `command.SetAction(Func<ParseResult, CancellationToken, Task>)` (not `SetHandler`)
- `parseResult.GetValue<T>(option)` to read values
- `root.Parse(args).InvokeAsync()` — invoke is on `ParseResult`, not `Command`

## Decisions made during implementation

**`Argument<T>` arity for prompt** — `ArgumentArity.ZeroOrOne` makes the positional
argument optional without requiring a separate `--prompt` flag. `phelix --cli "fix this"`
just works.

**`return 0` removed from `SetAction` lambda** — using the `Task`-returning overload
(not `Task<int>`) avoids the redundant explicit exit code. `InvokeAsync` returns 0 on
normal completion.

**`ImmutableArray<DisplayMessage>.Empty` → `[]`** — C# 14 collection expression, shorter
and avoids an explicit generic type.

**`--accepts-edits` and `--allow-all` declared on root** — they are silently ignored when
`--cli` is not set. Declaring them at root level (rather than as sub-options under a `cli`
subcommand) keeps the flag surface flat. Since both flags do nothing without `--cli`,
the help text makes their scope explicit.

## What was not changed

- `Phelix.Tui.csproj` — stays a class library, no `OutputType`, no new packages
- `TuiApprovalGate`, `TuiRenderer`, `TuiState`, `TuiEvent` — untouched
- `Phelix.Core` — untouched
- All 116 existing tests — pass without modification
