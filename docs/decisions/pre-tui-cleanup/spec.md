# Pre-TUI Cleanup

**Status:** Draft  
**Date:** 2026-06-05

---

## Problem

Before the TUI layer is introduced, several structural issues in the existing
codebase will compound — each one is manageable today but becomes a blocker
once a second caller (`Phelix.Tui`) joins the loop. This spec bundles five
discrete cleanup items into a single coherent delivery.

---

## Goal

Leave the codebase in a state where:

1. All source files are consistently formatted.
2. All naming follows the conventions in `AGENTS.md`.
3. `AgentLoop` returns complete, ready-to-use history — no caller assembly required.
4. `Program.cs` has one responsibility: running the REPL loop.
5. `ToolRegistry` does not repeat work on every turn.

No new features. No new interfaces. No new NuGet packages.

---

## Non-Goals

- `SessionEntry` schema redesign (tool call capture) — separate spec.
- `ToAITool()` boilerplate refactor — deferred until tool count grows.
- BashTool sandboxing — deferred to security phase.
- Config layer — separate spec.
- TUI work of any kind.

---

## Items

### Item 1 — Fix indentation in `Program.cs` and `PhelixTelemetry.cs`

Both files have their entire content indented with leading spaces — a copy-paste
artifact. The `using` directives and `namespace` declarations sit inside phantom
indentation that doesn't correspond to any block. This is cosmetically wrong and
will confuse any agent or developer reading cold.

**Change:** Re-indent both files so all top-level declarations start at column 0.
No logic changes.

---

### Item 2 — Fix `MaxturnsDefault` → `MaxTurnsDefault` in `AgentOptions`

`AgentOptions.cs` line 13 declares:

```csharp
const int MaxturnsDefault = 5;
```

The `t` is lowercase. The property it backs is `MaxTurns` (uppercase `T`).
This violates the project's naming standard: identifiers are sentence fragments
and must be self-documenting. `MaxturnsDefault` reads as a typo.

**Change:** Rename the constant to `MaxTurnsDefault`. One-line fix.

---

### Item 3 — Return complete history from `AgentLoop`

`AgentLoop.RunTurnAsync` currently returns a `Turn` whose `Messages` list
contains everything *sent to* the model this turn but does not include the
final assistant reply. The caller in `Program.cs` compensates by manually
appending it:

```csharp
List<ChatMessage> updatedHistory = new(completedTurn.Messages)
{
    new(ChatRole.Assistant, completedTurn.Response.Text ?? string.Empty)
};
conversationHistory = updatedHistory;
```

This is a leaky implementation detail. The caller has to know that `Turn.Messages`
is incomplete. When the TUI becomes a second caller, this knowledge has to be
duplicated or it will be wrong.

**The fix:** `AgentLoop` appends the final assistant message to `messages` before
returning, so `Turn.Messages` is the complete, ready-to-replay history. The caller
passes it back unchanged on the next turn — no reconstruction needed.

**Change in `AgentLoop.cs`:** In the early-exit branch (where `FinishReason !=
ToolCalls`), move `messages.Add(assistantMessage)` before the `return` statement.
It is currently placed after the span tags are set. Confirm the ordering is
consistent with the tool-call branch (which already appends before looping).

**Change in `Program.cs`:** Remove the manual history reconstruction block.
Replace with:

```csharp
conversationHistory = completedTurn.Messages;
```

---

### Item 4 — Extract bootstrapping out of `Program.cs`

`Program.cs` currently owns five distinct concerns:

1. OTel tracer setup
2. `OpenAIClient` / `IChatClient` construction
3. `AgentOptions` construction
4. `ToolRegistry` population
5. The REPL loop

Only the REPL loop belongs in `Program.cs`. The rest is bootstrapping — wiring
dependencies together before the loop starts. When the TUI layer arrives,
`Phelix.Tui` will need the same `IChatClient`, `AgentLoop`, and `ToolRegistry`
without duplicating the wiring.

**The fix:** Introduce a `PhilixHost` static class in `Phelix.Cli` that owns
steps 1–4 and exposes the constructed `AgentLoop` and any dependencies the loop
needs. `Program.cs` calls `PhelixHost.Build()` and receives what it needs to
run the loop.

**New file:** `src/Phelix.Cli/PhelixHost.cs`

```
PhelixHost
  + Build() → (AgentLoop agentLoop, TracerProvider? tracerProvider)
```

`Build` does exactly what `Program.cs` does today for steps 1–4 — moved verbatim,
not redesigned. The goal is separation, not abstraction. `PhelixHost` is
internal to `Phelix.Cli`; it is not exposed to `Phelix.Core`.

**`Program.cs` after the change:**

```csharp
(AgentLoop agentLoop, TracerProvider? tracerProvider) = PhelixHost.Build();
using TracerProvider? _ = tracerProvider;

List<ChatMessage> conversationHistory = new();

Console.WriteLine("Phelix — type 'exit' to quit.");
Console.WriteLine();

while (true)
{
    // REPL loop — unchanged
}

return 0;
```

---

### Item 5 — Cache `ToAITools()` result in `ToolRegistry`

`ToolRegistry.ToAITools()` is called once per model invocation. Each call
allocates a new `List<AITool>` and calls `ToAITool()` on every registered
tool. `ToAITool()` itself calls `AIFunctionFactory.Create`, which uses
reflection to build a parameter schema. This work is identical on every call —
the tool set does not change after registration.

**The fix:** Build the `List<AITool>` once, at `Register` time, and cache it.
`ToAITools()` returns the cached list.

**Change in `ToolRegistry.cs`:**

```csharp
private readonly List<AITool> _aiTools = new();

public void Register(ITool tool)
{
    if (!_tools.TryAdd(tool.Name, tool))
        throw new ArgumentException($"A tool named '{tool.Name}' is already registered.", nameof(tool));

    _aiTools.Add(tool.ToAITool());
}

public IList<AITool> ToAITools() => _aiTools;
```

The `All` property and `TryGet` are unchanged.

---

## File change summary

| File | Change |
|---|---|
| `src/Phelix.Cli/Program.cs` | Re-indent; remove history reconstruction block; call `PhelixHost.Build()` |
| `src/Phelix.Cli/PhelixHost.cs` | New file — owns OTel, client, options, and registry bootstrapping |
| `src/Phelix.Core/Agent/AgentOptions.cs` | Rename `MaxturnsDefault` → `MaxTurnsDefault` |
| `src/Phelix.Core/Agent/AgentLoop.cs` | Append final assistant message before returning `Turn` |
| `src/Phelix.Core/Tools/ToolRegistry.cs` | Cache `AITool` list; build at `Register` time |
| `src/Phelix.Core/Telemetry/PhelixTelemetry.cs` | Re-indent |

---

## Testing

- All 38 existing tests must pass unchanged. These changes must not require
  test modifications — if a test breaks, the change is wrong.
- Manual end-to-end: run Phelix, send a prompt that triggers at least one tool
  call, confirm the response and next turn both work correctly (history is intact).
- Confirm `Turn.Messages` on turn 2 contains the full history from turn 1 —
  user message, assistant reply, tool call(s), tool result(s), and the turn-1
  assistant reply.

---

## Open questions

None blocking implementation.
