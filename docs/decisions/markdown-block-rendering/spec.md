# Spec: Markdown Block Rendering

**Branch:** `feature/markdown-block-rendering`
**Status:** proposed

---

## Problem

Model text streams to the terminal one chunk at a time through raw
`Console.Write` (`CliRenderer.WriteChunk`). Two consequences:

1. **Words split mid-character.** Raw streaming has no word awareness, so the
   terminal hard-wraps at the cell boundary — `repository` becomes `rep` +
   `ository` across a line break.
2. **Markdown is dumped literally.** The model emits markdown; the terminal shows
   it raw. Backticks, dashes, `#`, and `|` appear as text. A comparison table
   arrives as a wall of pipes.

These are the same problem. Both are cured by rendering **complete text** instead
of raw characters: complete text can be word-wrapped, and complete text can be
parsed as markdown. Solving markdown rendering subsumes the wrap fix — no separate
word-wrap component is needed.

---

## Goal

Render the model's markdown to styled terminal output: headings, emphasis, inline
code, lists, links, fenced code blocks, horizontal rules, and **tables**. Output
stays append-only — no cursor control, no terminal ownership. `Phelix.Core` does
not change.

---

## Approach

Render per **text segment**, where a segment is the run of assistant text between
tool calls (and the final answer at the end of the turn).

The streaming pipeline accumulates chunks into a buffer. At each segment boundary
— a tool call is about to start, or the turn ends — the buffer is parsed by Markdig
and rendered through Spectre, then cleared. Output only ever flows forward.

This keeps every Markdig parse over a complete, coherent chunk of markdown, so
lists and tables render correctly. The cost is that prose appears one segment at a
time rather than character by character. Tool activity already streams live (grey
tool lines), so the perceived wait is mostly filled.

### Rejected alternatives

| Option | Why not |
|---|---|
| **Buffer the whole turn, render once at the very end** | Correct but least responsive; even short inter-tool narration would be withheld until the turn closes. |
| **Stream raw characters live, then rewind the cursor and re-render the block** | Requires owning the terminal (count physical lines, move cursor up, clear, repaint). Fragile across resize and scrollback. This is the `AnsiConsole.Live()` machine the ROADMAP defers until "visibly insufficient." Buys only character-by-character typing. |
| **Per-markdown-block flushing (cut the stream at every blank line)** | Splits loose lists across two Markdig parses and restarts ordered-list numbering. Avoiding that needs a heuristic mini-parser tracking list/fence state — the swamp we use Markdig to avoid. Deferred (see Upgrade path). |

---

## Dependencies

### `Phelix.Cli.csproj`

Add `Markdig`. Markdig parses markdown into a `MarkdownDocument` (a tree of typed
blocks and inlines); the renderer walks that tree. `Spectre.Console` is already
referenced.

Markdig pipeline: enable pipe tables.

```csharp
MarkdownPipeline pipeline = new MarkdownPipelineBuilder()
    .UsePipeTables()
    .Build();
```

> The building agent should confirm exact Markdig and Spectre.Console API surface
> against the installed package versions before implementing — type and method
> names below describe intent, not a frozen contract.

---

## Components

All new code lives in `Phelix.Cli`. Two pieces:

### `StreamingMarkdownWriter` (stateful, one per turn)

Owns the per-segment buffer and the not-a-terminal guard.

| Member | Behaviour |
|---|---|
| `Write(string chunk)` | If output is a terminal: append `chunk` to the buffer. If redirected: write `chunk` straight to `Console.Write` (see Non-TTY behaviour). |
| `Flush()` | If the buffer is non-empty and output is a terminal: pass the buffer to `MarkdownBlockRenderer.Render`, then clear the buffer. No-op otherwise. |

Constructed once per turn in `RunSingleTurnAsync`. Its `Write` replaces
`CliRenderer.WriteChunk` as the `OnChunk` callback.

### `MarkdownBlockRenderer` (stateless)

`Render(string markdown)` parses with Markdig, then iterates the document's blocks
and dispatches each by type. Dispatch is required because Spectre's structured
widgets do not compose with its inline-markup-string style — tables and fenced code
must be emitted as standalone `IRenderable` objects.

| Markdig block | Rendered as |
|---|---|
| `HeadingBlock` | Markup string, accent (indigo `#a78bfa`), bold. Level may map to weight/prefix; no font-size in a terminal. |
| `ParagraphBlock` | Markup string built from inline mapping (below), wrapped by Spectre to console width. |
| `ListBlock` / `ListItemBlock` | Indented items, accent bullet `•` (or `1.` for ordered), each item's inlines mapped. |
| `Table` (Markdig.Extensions.Tables) | Spectre `Table`, rounded border, header row styled. **Standalone renderable — not a markup string.** |
| `FencedCodeBlock` / `CodeBlock` | Spectre `Panel` containing the raw code, subtle border, no syntax highlighting in v1. **Standalone renderable.** |
| `ThematicBreakBlock` | Spectre `Rule` (grey). |

**Inline mapping** (for heading / paragraph / list item content): walk the block's
inline tree and translate to Spectre markup —

| Markdig inline | Markup |
|---|---|
| `LiteralInline` | escaped literal text (see below) |
| `EmphasisInline` (italic / bold) | `[italic]…[/]` / `[bold]…[/]` |
| `CodeInline` | styled inline code — distinct background/foreground |
| `LinkInline` | Spectre `[link=url]label[/]` |
| `LineBreakInline` | newline / space per type |

**Escaping is mandatory.** Model literal text can contain `[` and `]`, which
Spectre interprets as markup. Every `LiteralInline` (and any user/model-controlled
string) must pass through `Markup.Escape` before being placed into a markup string
— consistent with the existing `CliRenderer` escaping discipline. A table cell like
`[red]drop[/]` from the model must render as literal text, never as Spectre markup.

---

## Wiring changes

### `Program.cs` — `RunSingleTurnAsync`

```
var mdWriter = new StreamingMarkdownWriter();

TurnCallbacks callbacks = new(
    OnChunk:         mdWriter.Write,
    OnToolStarted:   (name, args) => { mdWriter.Flush(); CliRenderer.WriteToolStarted(name, args); },
    OnToolCompleted: CliRenderer.WriteToolCompleted
);

TurnResult result = await session.RunTurnAsync(userPrompt, callbacks, ct);

mdWriter.Flush();   // render the final segment
Console.WriteLine();
// …existing warning / error / separator handling…
```

The `OnToolStarted` wrapper flushes the current prose segment **before** the tool
line prints, preserving correct visual order (prose, then the tool it triggered).
The post-turn `Flush()` renders the final segment.

`CliRenderer.WriteChunk` is removed from the `OnChunk` path. It may be deleted or
kept for the redirected pass-through; `StreamingMarkdownWriter` handles that case
itself.

---

## Non-TTY behaviour

When output is redirected (`phelix "…" > notes.md`, piping into another process),
rendering ANSI styling and box-drawn tables into the stream is wrong — the consumer
wants clean markdown.

`StreamingMarkdownWriter` checks `Console.IsOutputRedirected` once at construction.
When redirected, `Write` passes chunks straight through unrendered and `Flush` does
nothing. The captured output is the model's original markdown, untouched.

(Spectre's `AnsiConsole` strips ANSI colour automatically when it detects a
non-terminal, but it would still draw tables and panels as ASCII art — hence the
explicit bypass rather than relying on capability detection alone.)

---

## Markdown subset — v1

| Feature | v1 | Later |
|---|:---:|:---:|
| Headings | ✓ | |
| Bold / italic | ✓ | |
| Inline code | ✓ | |
| Fenced code blocks (no highlighting) | ✓ | |
| Lists — ordered, unordered, nested | ✓ | |
| Links | ✓ | |
| Horizontal rules | ✓ | |
| **Tables** | ✓ | |
| Syntax highlighting inside code blocks | | ✓ |
| Blockquotes, task lists | | ✓ |

---

## Testing

No test project exists yet under `Phelix.Cli` (only `tests/Phelix.Core.Tests` exists
today). Create `tests/Phelix.Cli.Tests`, referencing `Phelix.Cli` and
`Spectre.Console.Testing`.

Assert on destinations — rendered output — not on call order.
`Spectre.Console.Testing` provides `TestConsole`, which captures rendered output as
inspectable text; inject it into `MarkdownBlockRenderer` in place of the global
`AnsiConsole`.

- **`StreamingMarkdownWriter`** — feed a chunk sequence, assert one render occurs on
  `Flush` over the full accumulated text and the buffer clears. Assert the
  redirected branch writes raw chunks and never renders.
- **`MarkdownBlockRenderer`** — feed representative markdown strings, assert the
  captured output reflects each construct: a heading carries the accent, a table
  produces border characters and the right columns, a fenced block is panelled,
  a bullet list is indented.
- **Escaping** — feed a paragraph and a table cell containing literal `[` `]`,
  assert they appear as literal text and do not alter styling.

No `Phelix.Core` tests change.

---

## What this does NOT change

- **`Phelix.Core`** — still streams chunks through `OnChunk`. No UI dependency,
  no new types, existing test suite (146 tests as of this writing) untouched.
- **Tool event lines, warnings, errors, turn separators** — already go through
  `CliRenderer` / Spectre; unchanged.
- **Approval prompts** — `InteractiveApprovalGate` still uses a plain `TextWriter`;
  styled prompts remain a separate follow-up.
- **No `AnsiConsole.Live()`, no cursor control.** Output stays append-only.

---

## Known tradeoffs

- A text segment with no internal tool calls (e.g. a long final answer) renders in
  one go when the turn ends. There is a silent gap during its generation. Accepted
  for v1; a streaming "thinking" indicator needs the stateful-renderer upgrade and
  is out of scope.
- No character-by-character typing. Deliberate — see Rejected alternatives.

---

## Upgrade path (future)

**Per-block incremental rendering.** To make paragraphs appear one at a time
*within* a segment, `StreamingMarkdownWriter` gains blank-line / fenced-code
boundary tracking and flushes a completed prefix of the buffer, dropping it after
render. The open problem to solve first: a loose list or ordered list straddling a
flush boundary must not be split across two Markdig parses (it restarts numbering).
Resolve before building.

**Thinking indicator + syntax highlighting.** Both arrive with the stateful
`CliRenderer` (`AnsiConsole.Live()`) milestone already noted in the ROADMAP — a
spinner in the silent gap, and tokenised colour inside fenced code blocks.
