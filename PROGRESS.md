# Phelix — Progress Tracker

## Current Phase
**Phase 2 — Close the Control Loop**
Goal: The agent can call a tool, get a result back, and use that result in the next turn.

---

## Done

### Session 1 — 2026-05-30
- Initialized git repo and `.gitignore`
- Created solution file (`phelix.sln`)
- Scaffolded three projects: `Phelix.Core` (classlib), `Phelix.Tui` (classlib), `Phelix.Cli` (console)
- Scaffolded test project: `Phelix.Core.Tests` (xunit)
- Wired project references (Cli → Core + Tui, Tui → Core, Tests → Core)
- Deleted placeholder files (`Class1.cs`, `UnitTest1.cs`)
- Added NuGet packages:
  - `Anthropic` 12.24.1 → `Phelix.Core` (official Anthropic SDK)
  - `Spectre.Console` 0.55.2 → `Phelix.Tui`
  - `System.CommandLine` 3.0.0-preview → `Phelix.Cli`
- Moved `ARCHITECTURE.md` into `docs/`
- Solution builds clean, 0 warnings, 0 errors

### Session 2 — 2026-05-31
- Wrote `AgentOptions.cs` — `record` with `ModelId`, `SystemPrompt`, `MaxTurns` (default 20)
- Wrote `Turn.cs` — `record` capturing `IReadOnlyList<ChatMessage>`, `ChatResponse`, `DateTimeOffset`
- Wrote `AgentLoop.cs` — single-turn loop; takes `IChatClient` + `AgentOptions`, builds message list, calls `GetResponseAsync`, returns a `Turn`
- Wired `Program.cs` — `AnthropicClient().AsIChatClient(modelId)` bridges the Anthropic SDK into `IChatClient`; reads `ANTHROPIC_API_KEY` from environment automatically
- Solution builds clean; end-to-end run pending API key

**Key discovery:** `AsIChatClient()` is an extension method in the `Microsoft.Extensions.AI` namespace shipped inside the `Anthropic` package itself — requires `using Microsoft.Extensions.AI` to resolve.

### Session 3 — 2026-06-01
- Wired OpenRouter as a drop-in `IChatClient` via `Microsoft.Extensions.AI.OpenAI` + `OpenAI` packages
- Added agent-first conventions to `AGENTS.md` (no `var`, XML docs, business-rule test names, deferred items)
- Added XML documentation to `AgentOptions`, `Turn`, and `AgentLoop`
- Added streaming to `AgentLoop.RunTurnAsync` via optional `Func<string, Task>? onChunk` callback
- Wrote `TerminalRenderer.WriteChunk` in `Phelix.Tui` — streams tokens to stdout inline
- First successful streaming end-to-end run

**Key discovery:** `Microsoft.Extensions.AI` uses `ChatResponseUpdate` (not `StreamingChatCompletionUpdate`) as the streaming update type. `ToChatResponse()` assembles a full `ChatResponse` from a collected list of updates.

---

### Session 4 — 2026-05-31
- Added `SessionEntry` record in `Phelix.Core/Session/` — serializable snapshot of one turn (user prompt, assistant text, model ID, timestamp)
- Added `SessionLogger` static class — `AppendAsync` writes one JSONL line per turn to `~/.phelix/sessions/YYYY-MM-DD.jsonl`; directory created on first write
- Wired `SessionLogger.AppendAsync` into `Program.cs` after each turn

**Key decision:** One file per calendar day — groups sessions naturally, no collision risk, files stay human-browsable without tooling.

---

### Session 4 (continued) — 2026-05-31
- Defined Phase 2 scope: multi-turn history, tool definition/registry, tool dispatch, `ReadFileTool`
- Updated `AGENTS.md` with format conventions (Markdown/XML/JSON/JSONL meta-rule) and working style (gate rule, code storytelling standard)
- Rewrote `Program.cs` as a REPL loop — reads from stdin, maintains `conversationHistory` across turns, exits on `exit` or Ctrl+C
- Fixed history bug: `conversationHistory` now built from `completedTurn.Messages` + appended assistant reply each turn

**Pending validation:** Multi-turn memory not yet confirmed working end-to-end due to OpenRouter 429 rate limits on free tier. Test first thing next session.

### Session 5 — 2026-06-01 (Phase 2)
- Wrote `ITool` interface — `Name`, `Description`, `ExecuteAsync`, `ToAITool()`
- Wrote `ToolRegistry` — `Register`, `TryGet`, `All`, `ToAITools()`
- Rewrote `AgentLoop.RunTurnAsync` with inner dispatch loop — detects `FinishReason == ToolCalls`, executes tools via registry, feeds `FunctionResultContent` back, loops until `Stop` or `MaxTurns`
- Wrote `ReadFileTool` — reads files within a bounded root; path-traversal safe; implements `ToAITool()` with typed delegate for correct MEAI schema
- Wired `ToolRegistry` + `ReadFileTool` into `Program.cs`
- Wired `ChatOptions.Tools` in `AgentLoop` via `toolRegistry?.ToAITools()` — model now receives tool definitions on every call
- Wrote 9 tests: `ToolRegistryTests` (register, lookup, duplicate, All) and `ReadFileToolTests` (happy path, missing param, outside root, not found, traversal attempt)

**Key discovery:** `ChatResponse.Message` does not exist — it is `ChatResponse.Messages` (list); the assistant reply is always `Messages[^1]`.

**Key decision:** `ToAITool()` lives on `ITool` rather than in `ToolRegistry` — each tool owns its parameter schema because `ToolRegistry` has no way to know a tool's parameter shape generically. `AIFunctionFactory.Create` reflects on the delegate signature to produce the JSON schema the model sees.

---

### Session 6 — 2026-06-02 (Phase 3 — Observability)
- Added OpenTelemetry tracing via `Microsoft.Extensions.AI` + OTLP exporter
- Wrote `PhelixTelemetry.cs` — `ActivitySource` and all span/tag name constants
- Wired turn span (`phelix.agent.turn`) and per-tool-call span (`phelix.tool.call`) in `AgentLoop`
- Accumulated input/output token counts across all model calls in a turn; written to span at return
- Registered `TracerProvider` in `Program.cs` — activated only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; zero-cost when disabled
- Verified end-to-end in Jaeger: parent/child nesting correct, token counts accurate

**Key discovery:** `UseOpenTelemetry` must be called on `ChatClientBuilder`, not on `IChatClient` directly. The correct pattern is `new ChatClientBuilder(...).UseOpenTelemetry(...).Build()`.

### Session 6 (continued) — 2026-06-02 (Phase 4 — Tool Expansion)
- Added `WriteFileTool`, `RunCommandTool`, `ListFilesTool`, `SearchCodeTool`
- Registered all four tools in `Program.cs`
- Wrote tests for all new tools (38 tests total across tool suite)
- Added GitHub Actions CI workflow — build and test on push/PR

**Key decision:** Specialized tools (`read_file`, `write_file`, `list_files`, `search_code`) are candidates for removal in favour of a single bash tool. Reasoning: minimal harness — bash is the right primitive, specialized tools become user-added extensions. Decision not yet locked; spec to be written in `docs/decisions/` when ready.

### Session 7 — 2026-06-03
- Fixed fake streaming in `AgentLoop` — replaced `GetResponseAsync` with `GetStreamingResponseAsync`; tokens now forwarded to `onChunk` as they arrive; updates aggregated via `ToChatResponse()` to preserve finish reason, usage, and tool call reconstruction
- Added `SessionLoggerTests` — two-turn mock session and standalone JSONL line parseability; no network required
- Fixed indentation in `Program.cs`; expanded system prompt to encourage tool use and concise responses
- PRs opened: #6 (streaming fix + session logger tests), #7 (CLI housekeeping)

**Key discovery:** `AddMessagesAsync` consumes the stream but discards the `ChatResponse` (no usage data). Manual iteration + `ToChatResponse()` is required to preserve finish reason and token counts.

---

## Up Next

- Lock decision on tool consolidation (bash tool replacing specialized tools) — write spec in `docs/decisions/` first
- Resilience pipeline: retry / circuit breaker via `ChatClientBuilder` middleware in `Program.cs`
- Config layer: move magic strings (`ModelId`, `SystemPrompt`) out of `Program.cs`

---

## Decisions Made

| Decision | Reason |
|---|---|
| `Anthropic` (official) over `Anthropic.SDK` (community) | Official package, ships `AsIChatClient()` extension in `Microsoft.Extensions.AI` namespace |
| `Microsoft.Extensions.AI.Abstractions` arrives transitively | No need to add it directly; `Anthropic` package pulls it in |
| `System.CommandLine` preview version | No stable release exists yet; preview is the current state of the library |
| `Turn` uses `ChatResponse` not `ChatCompletion` | `GetResponseAsync` returns `ChatResponse`; `ChatCompletion` is from the older `CompleteAsync` API |
| `AgentLoop` is stateless — history passed in by caller | Loop owns no state; easier to test and replay sessions |

## Decisions Deferred

| Decision | Notes |
|---|---|
| Session persistence store path | Likely `~/.phelix/sessions/` per architecture doc — confirm when we get there |
| Magic strings (`ModelId`, `SystemPrompt`) in `Program.cs` | Replace with constants/config when config layer is built in Phase 4 |
| `MaxTokens = 1024` hardcoded in built-in adapter | Should come from config; deferred to Phase 4 |
