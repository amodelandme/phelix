# Implementation Notes: CLI Output Formatting

## What was built

`Spectre.Console` 0.56.0 added to `Phelix.Cli.csproj`.

`CliRenderer.cs` extended with:

- `WriteToolStarted(toolName, args)` — grey dimmed `◆ tool_name arg=val …` line printed
  before each tool executes. Argument values are truncated at 60 characters. All
  tool names and values are passed through `Markup.Escape` to prevent Spectre markup
  injection from model-controlled strings.
- `WriteToolCompleted(toolName, status, duration)` — grey `✓ tool_name (Nms)` on
  success; red `✗ tool_name (Nms)` on failure or denial.
- `WriteTurnSeparator()` — grey `Rule` between REPL turns.
- `WriteError(message)` — red error line; replaces the plain `Console.WriteLine`
  in `Program.cs` error handling.
- `WriteWarning` upgraded from `Console.WriteLine` to `AnsiConsole.MarkupLine` in yellow.

`Program.cs` wires `OnToolStarted` and `OnToolCompleted` on `TurnCallbacks` and
calls `WriteTurnSeparator` after each completed turn.

## Why raw Console.Write is kept for the token stream

`AnsiConsole.Markup` parses the entire string before writing. For streamed single-
character chunks this would introduce measurable latency and risk mangling partial
Spectre markup sequences mid-stream. `Console.Write` writes immediately with zero
overhead. The two paths are intentionally separate.

## Security note

`Markup.Escape` is applied to all model-controlled strings (tool names, argument
keys, argument values) before they reach `AnsiConsole.MarkupLine`. Without this,
a tool name like `[red]evil[/]` would be interpreted as Spectre markup.

## What was not changed

`InteractiveApprovalGate` still uses a plain `TextWriter`. Styled approval prompts
require an `IRenderer` seam at the `Phelix.Core` boundary — tracked in ROADMAP.
