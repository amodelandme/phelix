# Spec: CliRenderer Chrome Accent Pass

**Branch:** `feature/cli-chrome-accents`
**Status:** proposed

---

## Problem

`MarkdownBlockRenderer` established an indigo accent (`#a78bfa`) for headings, bullets,
and links in rendered model text. `CliRenderer` — which owns all structural chrome
(tool events, separators, the REPL prompt, warnings, errors) — still renders entirely
in flat grey and white. The two systems share a screen but not a palette. The visual
result is that model content and harness chrome feel like they belong to different
applications.

Specific mismatches today:

| Surface | Current | Target |
|---|---|---|
| Tool started marker `◆` | `[grey]` | accent |
| Tool completed `✓` | `[grey]` | accent |
| Tool completed `✗` | `[red]` | keep red |
| Turn separator rule | `grey dim` | accent (dimmed) |
| Session name prompt label | `[grey dim]` | secondary (dim) |
| REPL prompt `>` | plain `Console.Write` | accent |
| App greeting `Phelix — type 'exit' to quit.` | plain `Console.Write` | accent on "Phelix", dim on the rest |

---

## Goal

Apply the indigo accent consistently to harness chrome so `CliRenderer` and
`MarkdownBlockRenderer` share a coherent palette. No new colors are introduced.
`Phelix.Core` does not change. The change is purely cosmetic — no delegate signatures,
no new types, no behavior differences.

---

## Palette

Two tones, both already present in `MarkdownBlockRenderer`:

| Token | Spectre value | Used for |
|---|---|---|
| **Accent** | `#a78bfa` | Active markers and prominent chrome: `◆`, `✓`, separator rule, REPL `>`, "Phelix" wordmark |
| **Dim** | `grey dim` | Subdued chrome: session prompt label, metadata (arg list), the greeting suffix " — type 'exit' to quit.", tool duration |

`✗` and `Warning:` stay red/yellow — they carry semantic urgency that the accent
would dilute.

---

## Changes

### `CliRenderer.cs`

#### `WriteToolStarted`

Current:
```csharp
AnsiConsole.MarkupLine($"[grey]  ◆ {Markup.Escape(toolName)}{argList}[/]");
```

After:
```csharp
AnsiConsole.MarkupLine($"[#a78bfa]  ◆ {Markup.Escape(toolName)}[/][grey dim]{argList}[/]");
```

The tool name gets the accent; the arg list stays dim. `◆` is the active-work signal
and earns the accent. Arguments are metadata — dim keeps the line from competing with
the prose above it.

#### `WriteToolCompleted`

Current:
```csharp
string indicator = status == ToolCallStatus.Succeeded ? "✓" : "✗";
string color     = status == ToolCallStatus.Succeeded ? "grey" : "red";
AnsiConsole.MarkupLine($"[{color}]  {indicator} {Markup.Escape(toolName)} ({ms})[/]");
```

After:
```csharp
if (status == ToolCallStatus.Succeeded)
    AnsiConsole.MarkupLine($"[#a78bfa]  ✓ {Markup.Escape(toolName)}[/] [grey dim]({ms})[/]");
else
    AnsiConsole.MarkupLine($"[red]  ✗ {Markup.Escape(toolName)} ({ms})[/]");
```

`✓` + tool name → accent. Duration → dim. `✗` path stays fully red — same rationale as
`WriteWarning`.

#### `WriteTurnSeparator`

Current:
```csharp
AnsiConsole.Write(new Rule().RuleStyle("grey dim"));
```

After:
```csharp
AnsiConsole.Write(new Rule().RuleStyle("#a78bfa dim"));
```

The separator is structural punctuation between turns. Accent at reduced brightness
threads it into the palette without making it loud.

#### `WritePromptLabel`

Current:
```csharp
AnsiConsole.MarkupLine($"[grey dim]{Markup.Escape(label)}[/]");
```

No change. The session-name prompt label is administrative chrome that appears once
at startup and has no relation to the interactive accent. `grey dim` is correct.

### `Program.cs`

#### REPL prompt `>`

Current:
```csharp
Console.Write("> ");
```

After:
```csharp
AnsiConsole.Markup("[#a78bfa]> [/]");
```

The `>` is the primary user-facing interaction point. It is the one piece of chrome
the user types against on every turn — it earns the accent.

#### App greeting

Current:
```csharp
Console.WriteLine("Phelix — type 'exit' to quit.");
Console.WriteLine();
```

After:
```csharp
AnsiConsole.MarkupLine("[#a78bfa]Phelix[/][grey dim] — type 'exit' to quit.[/]");
AnsiConsole.WriteLine();
```

"Phelix" is the wordmark; accent there. The instructional suffix is metadata — dim.

#### Session-name prompt `>`

Current:
```csharp
Console.Write("> ");
```

After:
```csharp
AnsiConsole.Markup("[#a78bfa]> [/]");
```

Same rationale as the REPL prompt — same visual treatment.

---

## What does NOT change

- `WriteWarning` — yellow is correct; accent would make warnings look like normal output.
- `WriteError` — red is correct for the same reason.
- `WritePromptLabel` — dim grey is right; this is administrative, not interactive.
- `BuildArgList` — format unchanged; only the call site splits name from args.
- All `Phelix.Core` code — zero changes.
- `MarkdownBlockRenderer` — no changes; this spec only touches `CliRenderer` and `Program.cs`.
- Test assertions in `Phelix.Cli.Tests` — the four `StreamingMarkdownWriter` tests and
  nine `MarkdownBlockRenderer` tests are unaffected (they don't touch `CliRenderer`).

---

## Contrast verification

The inline-code badge fix (`[grey on grey19]` → `[white on grey23]`) was missed on
first pass because `dotnet run` pipes stdout, and Spectre silently strips all ANSI
styling when the output is not a terminal. The verification method that caught it was:

```python
import pty, os, sys
master, slave = pty.openpty()
pid = os.fork()
if pid == 0:
    os.setsid()
    os.dup2(slave, 0); os.dup2(slave, 1); os.dup2(slave, 2)
    os.execv("/usr/bin/dotnet", ["dotnet", "run", "--project", "src/Phelix.Cli", "--", "…"])
    sys.exit(1)
os.close(slave)
data = b""
while True:
    try: data += os.read(master, 4096)
    except OSError: break
print(data.decode(errors="replace"))
```

For this pass, verify each changed surface by running `cat -v` on the pty output and
confirming:

- `◆` and `✓` emit `ESC[38;2;167;139;250m` (RGB 167,139,250 = `#a78bfa`).
- Tool name after `◆` is in accent, arg list reverts to dim grey.
- Separator rule uses the accent sequence at reduced intensity.
- REPL `>` emits accent before control returns to the user.
- "Phelix" in the greeting emits accent; the remainder emits dim grey.

Do not accept a non-TTY capture as confirmation.

---

## Testing

The existing `Phelix.Cli.Tests` suite (`MarkdownBlockRendererTests`, `StreamingMarkdownWriterTests`)
does not cover `CliRenderer`. This pass does not add `CliRenderer` tests — the surface is
narrow (each method is one `AnsiConsole.MarkupLine` call), the palette changes are
visually verified via PTY, and the only logic that could regress is `BuildArgList`, which
is already exercised indirectly.

If a `CliRendererTests` suite is added later, `CliRenderer` will need an
`IAnsiConsole`-injection seam (same pattern as `MarkdownBlockRenderer`) — that
refactor is out of scope here.

---

## Upgrade path

When `CliRenderer` eventually becomes a stateful object (the `AnsiConsole.Live()`
milestone noted in the ROADMAP for spinner/checkmark in-place updates), the accent
constants established here should be extracted to a shared `PhelixTheme` static class
so both `CliRenderer` and `MarkdownBlockRenderer` reference the same source of truth
rather than duplicating the `#a78bfa` string literal.
