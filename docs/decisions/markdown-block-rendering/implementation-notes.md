# Markdown Block Rendering — Implementation Notes

**Status:** Complete
**Date:** 2026-06-22
**Branch:** feature/markdown-block-rendering

---

## What was built

`StreamingMarkdownWriter` buffers streamed text per segment (between tool calls,
and the final answer) and flushes through `MarkdownBlockRenderer`, a stateless
dispatcher that walks Markdig's parsed block tree and renders headings, paragraphs,
lists (nested, ordered/unordered), tables, fenced code, and thematic breaks via
Spectre. All model-controlled literal text passes through `Markup.Escape`. The
spec's design landed without structural changes; one styling bug was found and
fixed during manual verification (see below).

---

## Files changed

| File | Change |
|---|---|
| `src/Phelix.Cli/MarkdownBlockRenderer.cs` | New — stateless block/inline dispatcher |
| `src/Phelix.Cli/StreamingMarkdownWriter.cs` | New — per-turn buffer, flush on segment boundary |
| `src/Phelix.Cli/Program.cs` | Modified — `OnChunk` now writes to `StreamingMarkdownWriter`; `OnToolStarted` flushes before printing the tool line; final flush after the turn completes |
| `src/Phelix.Cli/CliRenderer.cs` | Modified — removed now-dead `WriteChunk`; updated stale class remarks |
| `src/Phelix.Cli/Phelix.Cli.csproj` | Modified — added `Markdig`; added `InternalsVisibleTo` for `Phelix.Cli.Tests` |
| `tests/Phelix.Cli.Tests/` | New project — first test project for `Phelix.Cli`; `Spectre.Console.Testing` for output assertions |

---

## Decisions made during implementation

### `Phelix.Cli.Tests` did not exist — created it

The spec assumed `Spectre.Console.Testing.TestConsole` could be injected into
`MarkdownBlockRenderer`, but no test project existed under `Phelix.Cli` at all.
Created `tests/Phelix.Cli.Tests` mirroring `Phelix.Core.Tests`'s structure
(xunit, coverlet, `IsPackable=false`), referencing `Phelix.Cli` and
`Spectre.Console.Testing`.

### `InternalsVisibleTo` needed for testability

`MarkdownBlockRenderer` and `StreamingMarkdownWriter` stay `internal`, consistent
with the existing `CliRenderer` pattern. `Phelix.Cli.csproj` gained an
`InternalsVisibleTo` entry for `Phelix.Cli.Tests` rather than making these types
public.

### Markdig/Spectre API surface confirmed by reflection probe, not docs

Before writing the renderer, the exact installed API (Markdig 0.41.1, Spectre
0.56.0) was probed via a throwaway reflection harness against the restored NuGet
packages — block/inline type names, `EmphasisInline.DelimiterCount` (≥2 = bold,
else italic), `ListBlock.IsOrdered`, `Table`/`TableCell`/`TableRow` extension
methods (`AddColumn(string)`, `AddRow(string[])`, `Border(TableBorder)` are
extension methods on static `*Extensions` classes, not instance methods — show up
empty under plain reflection on the type itself). `Table` and `TableCell` collide
between `Markdig.Extensions.Tables` and `Spectre.Console`; resolved with type
aliases (`MarkdigTable`, `MarkdigTableCell`, `MarkdigTableRow`).

### `StreamingMarkdownWriter` takes an explicit `isRedirected` constructor overload

The primary constructor reads `Console.IsOutputRedirected` once, per spec. A
second internal constructor `(IAnsiConsole?, bool)` was added so tests can force
both the buffered and pass-through branches deterministically, without depending
on the test runner's actual stdout redirection state.

---

## Bug found during manual verification — inline code contrast

Unit tests passed on the first run, including escaping. Manual review of real CLI
output (screenshot) showed list items unreadable — a near-invisible text-on-dark
block around filenames like `.env`. Root cause: `CodeInline` was styled
`[grey on grey19]` — dim grey foreground on a near-black background. Both dark
values, low contrast against the already-dim bullet line. Fixed to
`[white on grey23]` — white foreground on a visible mid-grey badge.

**How this was actually verified:** plain-text capture of `dotnet run` output
(piping to a string, or grep-style assertions) is insufficient for visual review —
when stdout isn't a real terminal, Spectre's `AnsiConsole` auto-detects
"not a terminal" and silently strips all ANSI styling. The original smoke test
that "confirmed" rendering before this bug surfaced never actually exercised
colored/styled output — it always hit the no-color fallback path. Confirmed the
fix by forcing a real pseudo-terminal via Python's `pty.fork()` and inspecting the
raw escape sequences directly (`cat -v`), e.g. `ESC[38;5;15;48;5;237m.env ESC[0m`
— foreground 15 (white), background 237 (medium grey). Bold (`ESC[1m`) and the
indigo bullet accent (`ESC[38;2;167;139;250m`) were confirmed already correct at
the same time — the contrast bug was isolated to inline code.

**Lesson for future styling work:** verify color/contrast claims against raw ANSI
output captured through a real pty, not a piped or non-TTY capture. Logged as a
named step in the next roadmap item (CliRenderer chrome accent pass).

---

## What was deferred

Out of scope per the spec's non-goals, unchanged by this implementation:

- Syntax highlighting inside fenced code blocks
- Blockquotes, task lists
- Per-block incremental rendering within a segment (the loose-list/ordered-list
  flush-boundary problem noted in the spec's Upgrade path)
- Thinking indicator during the silent gap on long final answers
- `CliRenderer` chrome (prompt arrow, app name, tool icons, turn separator) does
  not yet share the indigo accent used by headings/bullets — tracked as the next
  roadmap item, "CliRenderer chrome accent pass"
