# Implementation Notes: Tiered Approval Friction

## Structure summary

Five new types in `Phelix.Core.Agent`:

- `ApprovalTier` (`Auto` / `Prompt` / `Confirm`) — declared on each `ITool`.
- `SessionMode` (`Default` / `AcceptsEdits` / `AllowAll`) — set at startup, controls gate behaviour.
- `IApprovalGate` — single method: `RequestApprovalAsync(toolName, tier, callSummary, ct)`.
- `AutoApproveGate` — always returns `true`. Used for `AllowAll` and in tests.
- `InteractiveApprovalGate` — prompts via injected `TextReader`/`TextWriter`. Tier × mode
  determines whether to skip silently, prompt with y/N, or require full "yes".

## Gate selection in `PhelixHost`

`BuildApprovalGate(SessionMode)` builds the right gate:
- `AllowAll` → `AutoApproveGate` + startup warning via `TerminalRenderer.WriteWarning`
- `Default` or `AcceptsEdits` → `InteractiveApprovalGate(mode, Console.In, Console.Out)`

The mode is passed to `PhelixHost.Build(SessionMode)` from `Program.cs`, which reads
`--allow-all` and `--accepts-edits` from `args`.

## Dispatch path in `AgentLoop`

Before calling `tool.ExecuteAsync`, `AgentLoop` now:
1. Calls `BuildCallSummary(call.Name, args)` — extracts `path`, `command`, or `query`
   (in that priority order) to give the user a concrete description of what the call
   will do.
2. Calls `options.ApprovalGate.RequestApprovalAsync(...)`.
3. If denied: records `ToolCallStatus.Denied` and returns the denial message to the
   model without calling `ExecuteAsync`.

## Why `ApprovalTier` is on `ITool`, not assigned at registration

Assigning tier at registration (in `PhelixHost`) would make the tier implicit —
visible only in one file, invisible to anyone reading the tool itself. Putting it on
the interface means a reader always knows what a tool requires. The tool's default
tier is part of its contract.

## `ToolCallStatus.Denied`

Added to `ToolCallStatus` so the session log captures denials as a distinct outcome.
The model receives a plain-English denial message so it can explain to the user that
the action was blocked, rather than treating silence as a bug.

## `InteractiveApprovalGate` testability

`TextReader`/`TextWriter` are injected at construction, not read from `Console`
directly. This is the standard seam for testing terminal interaction in .NET — no
interface wrapping needed. `StringReader("y")` / `StringWriter()` are sufficient for
all approval path tests.
