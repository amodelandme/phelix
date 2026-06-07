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

### ~~Session log: `list_files` glob scopes into `.git`~~ ✓ done
`ExcludedDirectories` property on `ListFilesTool` defaults to `{ ".git", "bin", "obj" }`. Segment-exact filtering applied after glob resolution. Description updated to guide toward scoped patterns. Spec and implementation in `docs/decisions/list-files-glob-scoping/`.

### Tool output truncation
Tool results are appended as-is. A `bash` call that dumps 20,000 lines consumes the context window. Every tool result — including `list_files` blobs — is sent verbatim on every subsequent turn.
- Add `TruncateToolOutput(string result, int maxChars)` helper in `AgentLoop`
- Applied before appending any tool result to the message list

### Context compaction
`conversationHistory` is an unbounded list — every turn sends the full history to the model. Accumulated tool results compound the cost significantly.
- Detect when history approaches a threshold (half the context window)
- Summarize older turns into a single condensed message; discard the raw turns

### Retry / circuit breaker
A 429 or transient network error kills the session.
- Exponential backoff wired into `ChatClientBuilder` middleware
- Write spec in `docs/decisions/` first

### AGENTS.md per-repo loading
Support loading a per-project `AGENTS.md` from the working directory on startup and injecting it into the system prompt.
- Small feature; no new infrastructure needed

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
