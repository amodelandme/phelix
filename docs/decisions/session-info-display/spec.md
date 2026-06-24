# Session Info Display — Model & Token Visibility

**Status:** Approved
**Phase:** Phase Queue
**Date:** 2026-06-24

---

## Problem

The CLI never shows the user which model is active or how many tokens a session
has consumed. All of this data already exists at runtime —
`Turn.Usage` (per-turn input/output tokens), `Turn.Response.ModelId`,
`PhelixSession.TotalTokenCount` (cumulative), and the active model/provider/mode
known at startup — but `Program.RunSingleTurnAsync` discards everything except
error and turn-limit handling before drawing the turn separator. The user is
flying blind on cost and on which model is answering.

---

## Goal

Surface model identity and token usage through three coordinated surfaces, all
sharing common formatting helpers:

1. **Startup banner** — printed once before the REPL begins: active model logical
   name, model id, provider, and session mode.
2. **Per-turn footer** — a dim line after each turn: model id, this turn's tokens
   (split input/output), and the running session total.
3. **On-demand `/status` command** — typed in the REPL to print the static facts
   (model, provider, mode) plus the current session total at any time.

Token counts are abbreviated (`4.8k`, `1.2k`, `340`). Per-turn detail is split
into input (`↑`) and output (`↓`); the session figure is a single combined total.

---

## Non-Goals

- Cost estimation in currency — token counts only; pricing tables are out of scope.
- A persistent/pinned status line above the prompt (considered and deferred — it
  is more rendering machinery than this iteration warrants; the footer plus
  `/status` cover the need).
- Live token counting mid-stream — counts are shown after the turn completes,
  using the usage the model reports.
- A general slash-command framework. `/status` is added as a minimal, explicit
  case alongside the existing `exit` handling, not a dispatch system. A broader
  command framework is a separate future decision.
- Changing what `PhelixSession` or `AgentLoop` expose — this is presentation only.

---

## Design

### Formatting helpers (CliRenderer)

Add a small set of static helpers to `CliRenderer`, kept there so all three
surfaces format identically:

- `FormatTokenCount(int)` → abbreviated string. `< 1000` prints the exact integer
  (`340`); `>= 1000` prints one decimal place with a `k` suffix (`1.2k`, `4.8k`),
  and `>= 1_000_000` uses `M` (`1.3M`). Rounding is to one decimal; trailing `.0`
  is trimmed (`5k`, not `5.0k`).
- The banner, footer, and `/status` lines are each rendered by a dedicated
  `CliRenderer` method so `Program.cs` stays declarative and the markup lives in
  one place (consistent with the existing `WriteToolStarted` / `WriteWarning`
  pattern).

### Startup banner

`Program.cs` already prints `Phelix — type 'exit' to quit.` before the REPL loop.
A new `CliRenderer.WriteSessionBanner(...)` prints the model/provider/mode block
immediately after that line. It receives the logical model name
(`config.ActiveModel`), the model id (`activeModel.ModelId`), the provider name
(`activeModel.Provider`), and the resolved `SessionMode`.

Because these values live in `PhelixHost.Build`, `Build` returns them to
`Program.cs` (extend the returned tuple with a small `SessionInfo` record:
logical name, model id, provider, mode). `Program.cs` passes that record to the
banner renderer. The single-turn (non-REPL) path does not print the banner —
it stays quiet for piped/scripted use.

### Per-turn footer

`RunSingleTurnAsync` already has the `TurnResult`. On
`TurnResult.Success`, after flushing markdown and before/with the separator, it
reads `success.Turn.Usage` and `success.Turn.Response.ModelId`, plus the
session's running `TotalTokenCount`, and calls
`CliRenderer.WriteTurnFooter(modelId, usage, sessionTotal)`. The line is dim and
sits with the separator:

```
claude-opus-4-8 · 1.2k↑ 340↓ · session 4.8k
```

`RunSingleTurnAsync` needs access to the session total. `PhelixSession.TotalTokenCount`
is already public; pass the `PhelixSession` (or the total) into the helper. The
footer is skipped when output is redirected, matching `StreamingMarkdownWriter`'s
behavior, so piped output stays clean. The footer is not printed on
`TurnResult.Failure` (the error line already renders) but is printed on a
turn-limit success.

### `/status` command

In the REPL loop, before the `exit` check is dispatched, recognize the exact
input `/status` (trimmed, case-insensitive). It prints
`CliRenderer.WriteStatus(sessionInfo, sessionTotal)` — the model/provider/mode
block plus the session total — then `continue`s without invoking a turn. Unknown
inputs starting with `/` are treated as normal prompts for now (no command
framework); only `/status` is special-cased.

---

## Contract

- The startup banner prints once, only in REPL mode (not single-turn), showing
  logical model name, model id, provider, and session mode.
- After each successful turn in REPL mode, a dim footer prints: model id, the
  turn's input and output tokens (split, abbreviated), and the abbreviated
  running session total.
- The footer is suppressed when stdout is redirected and on `TurnResult.Failure`.
- `/status` (case-insensitive, trimmed) prints model/provider/mode and the
  session token total without running a turn.
- `FormatTokenCount` renders `< 1000` exactly, `>= 1000` as `N.Nk` (trailing
  `.0` trimmed), `>= 1_000_000` as `N.NM`.
- No changes to `PhelixSession`, `AgentLoop`, `Turn`, or `UsageSummary` — the
  data they already expose is sufficient.
