# Session Info Display — Implementation Notes

**Date:** 2026-06-24
**Branch:** feature/session-info-display

---

## What shipped vs. the spec

Implemented as specced. All three surfaces (startup banner, per-turn footer,
`/status`) share `CliRenderer` helpers; no changes to `PhelixSession`,
`AgentLoop`, `Turn`, or `UsageSummary`.

---

## SessionInfo lives in Phelix.Cli, not Core

`SessionInfo` is a presentation carrier (model name, id, provider, mode) consumed
only by the banner and `/status`. It belongs to the CLI layer, so it sits in
`src/Phelix.Cli/SessionInfo.cs` and is returned as a fourth element of the
`PhelixHost.Build` tuple. Core types stayed untouched.

The session mode label (`default` / `accepts-edits` / `allow-all`) is rendered by
a private `CliRenderer.FormatMode` rather than `SessionMode.ToString()`, so the
user-facing wording is decoupled from the enum names.

## RunSingleTurnAsync now takes SessionInfo

The footer needs a model id even if `Turn.Response.ModelId` is null, so
`RunSingleTurnAsync` gained a `SessionInfo` parameter and falls back to
`sessionInfo.ModelId`. Both call sites (single-turn and REPL) pass it. The banner
is REPL-only — the single-turn path prints neither banner nor (via the
redirected-output guard) footer, keeping piped output clean.

## Footer self-suppresses when redirected

`WriteTurnFooter` checks `Console.IsOutputRedirected` internally and returns early,
matching `StreamingMarkdownWriter`'s behavior. This keeps the suppression rule in
one place instead of duplicating the guard at the call site.

## FormatTokenCount threshold quirk

Abbreviation is threshold-based: `< 1000` exact, `< 1_000_000` → `k`, else `M`,
each rounded to one decimal with a trailing `.0` trimmed. A consequence is that
`999_999` formats as `1000k` rather than `1M`, because `999999 / 1000 = 999.999`
rounds to `1000.0` while still under the 1,000,000 `M` threshold. This is
harmless for display and is pinned by a test so the behavior is intentional, not
accidental.

## Tests

`FormatTokenCount` is the only piece with real logic, so it has a
`CliRendererTests` theory covering exact values, `k`/`M` boundaries, trailing-zero
trimming, the `1000k` quirk, and negative clamping. The render methods are thin
markup wrappers over Spectre and were left to manual verification rather than
golden-output assertions.
