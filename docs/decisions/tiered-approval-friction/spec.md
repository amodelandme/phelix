# Spec: Tiered Approval Friction

## Problem

All tool calls currently auto-execute. A model running a `bash` command or writing a
file does so without any opportunity for the developer to review or deny it. For an
agentic session touching real files and running real shell commands, this is too
permissive as a default.

At the same time, requiring approval for every tool call — including read-only
operations — would make the harness unusable. The right answer is tiered friction:
reads are silent, writes ask, shell commands require explicit confirmation.

Separately, developers sometimes need to give the agent full autonomy for a session.
Forcing approval prompts then actively impedes their workflow. An escape hatch must
exist, but it must be visible and deliberate — not the default.

## Decision

Each `ITool` implementation declares an `ApprovalTier`. The `AgentLoop` consults an
`IApprovalGate` before every tool dispatch. The gate's behaviour is controlled by the
active `SessionMode`, which is set once at startup.

## Tiers

| Tier | Tools | Behaviour |
|---|---|---|
| `Auto` | `read_file`, `list_files`, `search_code`, `search_session` | Always executes silently |
| `Prompt` | `write_file` | Pauses; user types `y`/`yes` to allow. Default-deny |
| `Confirm` | `bash` | Pauses; user must type `yes` in full to allow. Default-deny |

`Prompt` uses default-deny (Enter alone rejects) because a file write is recoverable
but surprising. `Confirm` requires `yes` in full because a shell command can be
irreversible.

## Session modes

| Mode | Flag | Behaviour |
|---|---|---|
| `Default` | _(none)_ | `Auto` silent; `Prompt` asks y/N; `Confirm` requires "yes" |
| `AcceptsEdits` | `--accepts-edits` | `Auto` and `Prompt` silent; `Confirm` requires "yes" |
| `AllowAll` | `--allow-all` | Everything executes. Warning printed at session start |

`AcceptsEdits` mirrors the Claude Code concept: the developer has accepted that the
model will write files. Shell execution still stops for confirmation.

`AllowAll` collapses to `AutoApproveGate` — the same gate used for testing. The
distinction from `Default` is only the startup warning. Responsibility shifts to the
developer.

## Design

- `ApprovalTier` — enum on `ITool`. Self-documenting: any reader can see what a tool
  requires without tracing through config or registration code.
- `IApprovalGate` — interface consulted by `AgentLoop` before every dispatch.
  Swappable: `AutoApproveGate` for tests and allow-all, `InteractiveApprovalGate` for
  interactive sessions.
- `InteractiveApprovalGate` — takes `TextReader`/`TextWriter` constructor parameters
  so tests can inject `StringReader`/`StringWriter` without a real terminal.
- `SessionMode` — enum stored on `AgentOptions`. `PhelixHost` builds the correct gate
  from the mode. `AgentLoop` never knows which mode was selected.
- `ToolCallStatus.Denied` — new enum value recording when a call was denied. The model
  receives `"Tool call 'X' was denied by the user."` as its tool result so it can
  respond gracefully.

## Files changed

- `src/Phelix.Core/Agent/ApprovalTier.cs` — new
- `src/Phelix.Core/Agent/SessionMode.cs` — new
- `src/Phelix.Core/Agent/IApprovalGate.cs` — new
- `src/Phelix.Core/Agent/AutoApproveGate.cs` — new
- `src/Phelix.Core/Agent/InteractiveApprovalGate.cs` — new
- `src/Phelix.Core/Agent/AgentOptions.cs` — add `ApprovalGate` property
- `src/Phelix.Core/Agent/AgentLoop.cs` — gate check before dispatch; `BuildCallSummary` helper
- `src/Phelix.Core/Tools/ITool.cs` — add `ApprovalTier` property
- `src/Phelix.Core/Tools/*.cs` — implement `ApprovalTier` on all tools
- `src/Phelix.Core/Session/ToolCallStatus.cs` — add `Denied`
- `src/Phelix.Cli/PhelixHost.cs` — `BuildApprovalGate`; `Build(SessionMode)`
- `src/Phelix.Cli/Program.cs` — parse `--allow-all` / `--accepts-edits`
- `src/Phelix.Tui/TerminalRenderer.cs` — add `WriteWarning`
- `tests/Phelix.Core.Tests/Agent/ApprovalGateTests.cs` — new
- `tests/Phelix.Core.Tests/Agent/Fakes.cs` — add `ApprovalTier` to `FakeTool`
- `tests/Phelix.Core.Tests/Tools/ToolRegistryTests.cs` — add `ApprovalTier` to `StubTool`
