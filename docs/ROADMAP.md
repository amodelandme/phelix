# Phelix — Roadmap

## MVP Blockers
*Resolve before any TUI work begins.*

### ~~1. Turn limit feedback~~ ✓ done — PR #10
`TurnExitReason` enum on `Turn`; `[turn limit reached]` printed in CLI when `MaxTurns` is hit.

### ~~2. Thinking indicator~~ → deferred to TUI phase
Spinner from prompt submission to first token. Moved — implement as part of the Rich TUI milestone alongside `TerminalRenderer`.

### ~~3. Config layer~~ ✓ done — PR #11
YAML config at `~/.phelix/config.yaml`; named provider and model profiles; `IConfigProvider` seam for TUI. Falls back to defaults when absent.

---

## Phase Queue
*Well-scoped items, roughly in priority order. Start a spec in `docs/decisions/` before touching code.*

### ~~Session schema redesign~~ ✓ done
`SessionEntry` replaced by `TurnRecord`. Tool calls, token usage, exit reason, turn/session IDs, and start/end timestamps are now fully persisted. `bool` fields replaced with typed enums (`ToolCallStatus`, `SensorStatus`). `TurnEvent` hierarchy reserved for Phase 3 sensor results. Spec and implementation in `docs/decisions/session-schema-redesign/`.

### ~~Session log: enum serialization~~ ✓ done
`[JsonConverter(typeof(JsonStringEnumConverter))]` applied to `TurnExitReason`, `ToolCallStatus`, and `SensorStatus`. Enums now serialize as `"Completed"`, `"Succeeded"`, etc. instead of integers.

### ~~Session log: `list_files` glob scopes into `.git`~~ ✓ done
`ExcludedDirectories` property on `ListFilesTool` defaults to `{ ".git", "bin", "obj" }`. Segment-exact filtering applied after glob resolution. Description updated to guide toward scoped patterns. Spec and implementation in `docs/decisions/list-files-glob-scoping/`.

### ~~Tool output truncation~~ ✓ done
`TruncateToolOutput` helper on `AgentLoop` caps every tool result at 2,000 characters using an 80/20 head/tail split before the result reaches the model or the session log. `list_files` also returns relative paths (PR #16). The ephemeral tool pattern (`ContextMessages` on `Turn`) strips raw tool exchange messages from history after each turn, preventing prior-turn tool output from compounding across the context window. Spec and implementation in `docs/decisions/tool-output-truncation/`.

### ~~Context compaction + session continuity~~ ✓ done
`conversationHistory` compacts when estimated token count crosses `CompactionThresholdTokens` (default 40,000). Every turn is persisted to SQLite in real time. On compaction, history is replaced with a model-generated summary reconstructed from SQLite. The `search_session` tool lets the model query FTS5-indexed tool outputs from earlier in the session on demand. Spec and implementation in `docs/decisions/context-compaction/`.

### ~~Retry / circuit breaker~~ ✓ done
`RetryingChatClient` middleware in the `ChatClientBuilder` pipeline. Exponential backoff with ±20% jitter; retries on 429, 5xx, `TaskCanceledException`, `TimeoutException`, `IOException`. Streaming responses are buffered per attempt — no partial output on retry. Per-model `RetryPolicy` override with global fallback in config. Spec and implementation in `docs/decisions/retry-circuit-breaker/`.

### ~~AGENTS.md per-repo loading~~ ✓ done — PR #21
`AgentsMdLoader` reads `AGENTS.md` from the current working directory on startup and composes it with the base system prompt using XML-tagged sections. File absence is silent; read failures warn to stderr. Spec and implementation in `docs/decisions/agents-md-loading/`.

### Tiered approval friction
All tool calls currently auto-execute.
- Auto-approve low-risk reads (`ReadFileTool`, `ListFilesTool`)
- Prompt on writes (`WriteFileTool`)
- Require explicit confirmation for destructive or network-touching `BashTool` calls
- Configurable per user

---

## Backlog
*Good ideas that need a spec and the right moment. Not yet actionable.*

### Structured loop: Plan → Execute → Verify
The agent retries from scratch on failure. The right pattern is explicit plan-before-execute.
- Start with a system prompt change: instruct the agent to write a plan as its first action for non-trivial tasks, then execute step by step
- No new code required to start

### BM25 / inverted index for search
`SearchCodeTool` does O(n) line-by-line file scans. On a large repo this will slow noticeably.
- BM25 with an inverted index (e.g., Lucene.NET) for fast indexed lookups
- Do not replace the current approach until repo size is actually a problem

### Semantic / embedding search
`SearchCodeTool` handles only exact-string or regex matches. Queries like "error handling for authentication" return nothing if the code uses different vocabulary.
- Embedding-based search (text-embedding-3-small or local ONNX model) for conceptual queries
- Add as an optional mode alongside BM25, not a replacement

### Codebase knowledge graph (Roslyn-based)
The agent orients itself by reading raw files — expensive and shallow. A semantic graph built from the codebase gives the agent a compact, queryable map of the project structure.
- Walk `.cs` files via Roslyn's semantic model (not tree-sitter) — captures classes, methods, fields, call edges, inheritance, and symbol bindings with full type resolution
- Parse `.csproj` / solution files for project dependency graph and NuGet references
- Index DI registrations, attribute routing, and middleware pipeline (patterns invisible to AST-only tools)
- Expose via a `search_graph` tool: the agent queries the graph instead of reading raw files
- Pipeline: detect → extract → build graph → cluster → analyze → export (JSON + optional HTML)
- Reference: graphify (https://github.com/safishamsi/graphify) — Python/tree-sitter tool that proved the pattern at scale (61.5k stars, 71.5x token reduction on large repos). C# support exists but is surface-level; Roslyn gives significantly deeper semantics. No Python dependency needed.

### Skills system
No mechanism to externalize expertise or reuse step-by-step instruction sets across sessions.
- `~/.phelix/skills/` directory; load/invoke mechanism
- Evaluate each skill empirically with and without — stale or redundant skills hurt performance
- Reference: Cursor replaced 15,000 lines of orchestration code with a 200-line skill file

### Evaluation discipline
No systematic way to compare harness performance with vs. without a skill, prompt change, or tool.
- Needed before the harness matures beyond personal use

### Durable cross-session memory
No cross-session memory exists. Deferred until after the skills layer is in place.

---

## Vision
*Directional ideas. No commitment, no timeline.*

### Agent diagnostics
Analyze a completed session to surface where mistakes were made, where the agent got stuck, where instructions were unclear, and how many times the agent changed direction after discovering information it could have searched for earlier.

Narrower version: alert when a model attempts a tool call unsuccessfully multiple times before searching for a solution — then guide it to search first.

### Reflection / Critic pattern
A primary agent produces output; a critic agent reviews it and returns feedback; the primary agent revises.
- Most consistently effective multi-agent pattern per reference literature
- Implement only after single-agent quality is maxed out

### Orchestrator / Worker / Validator pipeline
For long tasks: an orchestrator decomposes work, delegates to worker agents, validates results.
- Start serial (orchestrator → worker → validator) before adding parallelism
- Single-agent path remains the default; multi-agent is opt-in escalation

### Rich TUI
Two-panel layout (left ~75% conversation, right ~25% sidebar: task title, token stats, tool activity, task checklist with live checkmarks). Intro screen: pure black, centered wordmark, single focused input with accent border, status line at bottom. Near-monochrome palette, single accent color used sparingly.
- Design reference: OpenCode (Go/Charm stack) — implement in .NET with Spectre.Console
- Begins after all MVP blockers are resolved

### Advanced UI (far future)
- Right panel: code view with AI-assisted inline editing, syntax highlighting, step-by-step spec walkthroughs
- Voice communication
