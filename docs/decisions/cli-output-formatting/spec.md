# Spec: CLI Output Formatting

**Branch:** `feature/cli-output-formatting`
**Status:** approved

---

## Problem

All terminal output currently uses `Console.Write` / `Console.WriteLine` — model
text, tool events, approval prompts, warnings, and errors are visually identical.
There is no way to distinguish what the model said from what the harness did.

---

## Goal

Give each output type a distinct visual identity. A developer glancing at the
terminal should immediately know:

- White text = model response
- Dimmed grey = tool activity (infrastructure noise, not content)
- Yellow = something needs attention (warning or approval)
- Red = failure

---

## Palette

| Element | Color | Reason |
|---|---|---|
| Model text (streaming) | default (white) | Primary signal — visually dominant |
| Tool started | `grey` dimmed | Infrastructure; should recede |
| Tool completed | `grey` dimmed | Same zone as started |
| Turn separator | `grey` dimmed | Structural chrome |
| Warning | `yellow` | Universal convention for non-fatal alerts |
| Approval prompt border + label | `yellow` | "Attention required" — same accent |
| Error | `red` | Universal convention for failures |

Single accent color: **yellow**. Nothing competes with model text.

---

## Changes

### `Phelix.Cli.csproj`

Add `Spectre.Console`. Keep raw `Console.Write` for the live token stream
(latency-sensitive). Use `AnsiConsole` for all structural elements.

### `CliRenderer.cs`

Add three new rendering methods:

| Method | When called | Output |
|---|---|---|
| `WriteToolStarted` | `OnToolStarted` callback | `  ◆ tool_name arg=val …` in grey |
| `WriteToolCompleted` | `OnToolCompleted` callback | `  ✓ tool_name (123ms)` in grey |
| `WriteTurnSeparator` | after each turn in the REPL loop | thin grey rule |

Upgrade existing methods:

| Method | Change |
|---|---|
| `WriteWarning` | `AnsiConsole.MarkupLine` in yellow instead of plain `Console.WriteLine` |
| Error output in `RunSingleTurnAsync` | `AnsiConsole.MarkupLine` in red |

### `Program.cs`

Wire `OnToolStarted` and `OnToolCompleted` on `TurnCallbacks`. Call
`CliRenderer.WriteTurnSeparator` after each completed turn in the REPL loop.

---

## What this does NOT change

- `Console.Write` for the token stream — zero latency impact
- `InteractiveApprovalGate` — stays in `Phelix.Core`, no UI dependency.
  Its plain `TextWriter` prompts are acceptable for now; styled approval
  prompts are a follow-up (requires an `IRenderer` seam at the Core boundary).
- All existing tests — no Core changes

---

## Upgrade path (future)

When in-place spinner updates and a persistent footer are needed, `CliRenderer`
becomes a stateful class (not static) with a `Start()` / `Stop()` lifecycle.
The callback signatures stay identical — only the internal rendering mechanism
changes. See ROADMAP for the full note.

---

## Out of scope

- Spinner / in-place cursor updates
- Persistent footer / status bar
- Styled approval prompts (requires `IRenderer` seam across Core boundary)
