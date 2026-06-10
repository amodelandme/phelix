# Dynamic Tool Loading — Feature Spec

**Status:** Future — do not implement until tool count reaches ~15 or schema tokens
become a measurable session cost  
**Phase:** Backlog  
**Date:** 2026-06-09

---

## Context

All tool schemas are registered at startup and sent to the model on every turn
via `ChatOptions.Tools`. At ~160 tokens per tool, a 6-tool registry costs ~960
tokens per request — roughly 98% of baseline turn cost on a minimal session.
This is currently acceptable.

The cost compounds as tool count grows. At 15+ tools (~2,400 tokens/turn) or
with richer schemas (parameter descriptions, enum values), the fixed floor becomes
the dominant per-turn expense and warrants a structural fix.

This spec describes the design to implement when that threshold is crossed. Nothing
here should be built until the problem is actually measured.

---

## Problem

`ToolRegistry.ToAITools()` returns the full cached schema list. `AgentLoop` passes
this list unchanged to `chatClient.GetStreamingResponseAsync` on every turn:

```csharp
ChatOptions chatOptions = new()
{
    ModelId = options.ModelId,
    Instructions = options.SystemPrompt,
    Tools = toolRegistry?.ToAITools()   // all schemas, every turn
};
```

The model receives schemas for tools it may never use in a given turn. On a session
focused entirely on reading files, write and bash schemas consume tokens with no
benefit.

---

## Goals

1. Reduce per-turn schema token cost without removing tools from the registry.
2. Keep all tools reachable — the model must be able to discover and use any
   registered tool within a single turn.
3. Preserve the existing `ITool` / `ToolRegistry` / `AgentLoop` interfaces; no
   breaking changes to the dispatch path.
4. Stay within the `Microsoft.Extensions.AI` abstraction — no raw HTTP calls to
   the Anthropic API.

---

## Non-Goals

- Semantic / embedding-based tool selection (deferred — over-engineered for
  current tool count; revisit if catalog exceeds 30+ tools)
- Using the Anthropic `defer_loading` API feature (requires dropping below
  `IChatClient`; not worth the coupling at this scale)
- Removing tools from the registry at runtime (tools are statically registered
  per session)
- Changing tool approval tiers or the dispatch path in `AgentLoop`

---

## Design

### Approach: two-phase turn with catalog tool

Phase 1 gives the model a lightweight tool catalog — name and one-line description
only — instead of full schemas. When the model identifies a tool it needs, it calls
`load_tool` to retrieve the full schema. `AgentLoop` injects the schema into the
next request. Subsequent calls to that tool proceed normally for the rest of the
turn.

This adds at most one extra model round-trip per turn (the catalog lookup). On
turns where the model already knows which tools it needs from prior context, the
catalog call may be skipped entirely.

### Catalog format

`load_tool` returns a JSON array of objects with `name` and `description` only:

```json
[
  { "name": "read_file",     "description": "Read the contents of a file." },
  { "name": "write_file",    "description": "Write or create a file." },
  { "name": "bash",          "description": "Execute a bash command." },
  { "name": "list_files",    "description": "List files matching a glob pattern." },
  { "name": "search_code",   "description": "Search for text or regex in source files." },
  { "name": "search_session","description": "Search earlier session tool call history." }
]
```

Approximately 80 tokens for a 6-tool catalog vs. ~960 tokens for full schemas —
an 8× reduction on the baseline turn.

### Permanent vs. deferred tools

Not all tools need to be deferred. Tools used on nearly every turn (e.g.
`read_file`, `list_files`) should remain in `ChatOptions.Tools` permanently to
avoid the catalog round-trip on the common path.

A new property on `ITool` controls this:

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    ApprovalTier ApprovalTier { get; }
    bool AlwaysLoad { get; }            // new — default false

    Task<string> ExecuteAsync(
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken);

    AITool ToAITool();
}
```

`ToolRegistry` partitions tools at registration time:

```csharp
public IList<AITool> AlwaysLoadedTools()  =>  tools where AlwaysLoad == true
public IList<AITool> DeferredTools()      =>  tools where AlwaysLoad == false
public ToolCatalog   BuildCatalog()       =>  name + description for all tools
```

Initial assignments:

| Tool | AlwaysLoad |
|---|---|
| `read_file` | true |
| `list_files` | true |
| `search_code` | true |
| `search_session` | true |
| `write_file` | false |
| `bash` | false |

Rationale: write and bash are used less frequently and carry approval overhead;
deferring them saves tokens on read-heavy turns without meaningfully affecting
the model's ability to use them when needed.

### `LoadToolTool`

A new internal tool, registered alongside the others but never deferred:

```csharp
public sealed class LoadToolTool(ToolRegistry registry) : ITool
{
    public string Name => "load_tool";
    public string Description =>
        "Load the full schema for one or more tools before using them. " +
        "Call this when you need to use a tool whose schema you do not have. " +
        "Pass the name of the tool. Returns the full parameter schema.";
    public ApprovalTier ApprovalTier => ApprovalTier.Auto;
    public bool AlwaysLoad => true;

    public Task<string> ExecuteAsync(
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        string toolName = parameters["tool_name"]?.ToString() ?? string.Empty;

        if (!registry.TryGet(toolName, out ITool? tool))
            return Task.FromResult($"Unknown tool: {toolName}");

        // Return schema description as text — AgentLoop picks this up and injects
        // the AITool into the next model call.
        return Task.FromResult(tool.ToAITool().JsonSchema?.ToString() ?? string.Empty);
    }
}
```

When `AgentLoop` sees a `load_tool` result, it adds the named tool's `AITool` to
the active `ChatOptions.Tools` list for the remainder of the turn. This injection
is handled in the tool-result processing block, before the next model call within
the same turn loop.

### `AgentLoop` changes

The turn loop gains a mutable tool set initialized from `AlwaysLoadedTools()`:

```csharp
List<AITool> activeTools = [.. toolRegistry.AlwaysLoadedTools()];

ChatOptions chatOptions = new()
{
    ModelId = options.ModelId,
    Instructions = options.SystemPrompt,
    Tools = activeTools
};
```

After processing each `load_tool` result, the named tool's `AITool` is appended to
`activeTools` and `chatOptions` is rebuilt with the updated list before the next
model call within the turn:

```csharp
if (call.Name == "load_tool" && loadedToolName != null)
{
    if (toolRegistry.TryGet(loadedToolName, out ITool? deferred))
        activeTools.Add(deferred.ToAITool());
}
```

`activeTools` is scoped to a single turn. The next turn starts fresh from
`AlwaysLoadedTools()`. This prevents schema accumulation across turns.

### System prompt change

The system prompt gains one paragraph instructing the model to use `load_tool`
before calling an unfamiliar tool:

```
You have access to a tool catalog. If you need to use a tool whose parameters
you don't know, call load_tool with the tool's name first. This loads its full
schema so you can call it correctly. Commonly used tools (read_file, list_files,
search_code, search_session) are always available.
```

---

## Component map

```
Phelix.Core/Tools/
    ITool.cs                — gains AlwaysLoad property (default false)
    ToolRegistry.cs         — gains AlwaysLoadedTools(), DeferredTools(), BuildCatalog()
    LoadToolTool.cs         — new; always-loaded; injects deferred schema on demand

Phelix.Core/Agent/
    AgentLoop.cs            — initializes activeTools from AlwaysLoadedTools();
                              appends deferred AITool on load_tool result

Phelix.Cli/
    PhelixHost.cs           — registers LoadToolTool; passes updated system prompt

Phelix.Core/Agent/
    AgentOptions.cs         — system prompt addition for load_tool instruction
```

---

## Contract

### `ITool.AlwaysLoad`
- Default implementation returns `false`. Tools that override to `true` are
  included in every model call with no catalog round-trip.
- `LoadToolTool` hardcodes `true` — it must always be present.

### `ToolRegistry`
- `AlwaysLoadedTools()` always includes `LoadToolTool` and any tool with
  `AlwaysLoad == true`.
- `BuildCatalog()` includes all registered tools, including deferred ones.
  The model uses the catalog to know what tools exist before loading schemas.

### `AgentLoop`
- `activeTools` is rebuilt per turn — no cross-turn schema accumulation.
- If `load_tool` is called for an already-active tool, the add is a no-op
  (or the duplicate is silently ignored).
- If `load_tool` names an unknown tool, the error string returned to the model
  is sufficient — no exception is thrown.

---

## Token budget (projected)

| Scenario | Current | After |
|---|---|---|
| Read-only turn (no write/bash needed) | ~960 tokens | ~320 tokens (~4 always-loaded tools + catalog) |
| Write turn (write_file loaded once) | ~960 tokens | ~480 tokens (catalog + load_tool + write schema) |
| Full tool turn (all tools) | ~960 tokens | ~960 tokens (degrades to current) |

Worst case equals the current baseline. Common read-heavy sessions save ~65%.

---

## Tests

### `ToolRegistry`
- `AlwaysLoadedTools_ReturnsOnlyAlwaysLoadTools`
- `DeferredTools_ReturnsOnlyNonAlwaysLoadTools`
- `BuildCatalog_IncludesAllRegisteredTools`

### `LoadToolTool`
- `ExecuteAsync_ReturnsSchema_ForKnownTool`
- `ExecuteAsync_ReturnsError_ForUnknownTool`

### `AgentLoop`
- `RunTurnAsync_StartsWithAlwaysLoadedToolsOnly`
- `RunTurnAsync_InjectsDeferredTool_AfterLoadToolCall`
- `RunTurnAsync_DoesNotAccumulateTools_AcrossTurns`

---

## Files

### New
```
src/Phelix.Core/Tools/LoadToolTool.cs
tests/Phelix.Core.Tests/Tools/LoadToolToolTests.cs
tests/Phelix.Core.Tests/Agent/DynamicToolLoadingTests.cs
```

### Modified
```
src/Phelix.Core/Tools/ITool.cs          — add AlwaysLoad property
src/Phelix.Core/Tools/ToolRegistry.cs   — add AlwaysLoadedTools(), DeferredTools(), BuildCatalog()
src/Phelix.Core/Agent/AgentLoop.cs      — activeTools per-turn; load_tool injection
src/Phelix.Cli/PhelixHost.cs            — register LoadToolTool; updated system prompt
```

### Unchanged
```
src/Phelix.Core/Agent/AgentOptions.cs
src/Phelix.Core/Agent/TurnCallbacks.cs
src/Phelix.Core/Tools/ReadFileTool.cs
src/Phelix.Core/Tools/WriteFileTool.cs
src/Phelix.Core/Tools/BashTool.cs
src/Phelix.Core/Tools/ListFilesTool.cs
src/Phelix.Core/Tools/SearchCodeTool.cs
src/Phelix.Core/Tools/SearchSessionTool.cs
```

---

## Implementation order

1. Add `AlwaysLoad` to `ITool` with default `false`; update all existing tools
2. Add `AlwaysLoadedTools()`, `DeferredTools()`, `BuildCatalog()` to `ToolRegistry`
3. Write `ToolRegistry` tests — all passing before continuing
4. Implement `LoadToolTool`
5. Write `LoadToolToolTests`
6. Update `AgentLoop` to initialize `activeTools` from `AlwaysLoadedTools()` and
   inject deferred schemas after `load_tool` results
7. Write `AgentLoop` dynamic loading tests
8. Update `PhelixHost` to register `LoadToolTool` and add system prompt instruction
9. Measure token reduction on a real session; adjust `AlwaysLoad` assignments if needed
