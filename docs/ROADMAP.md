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

### ~~CLI output formatting~~ ✓ done
`Spectre.Console` added to `Phelix.Cli`. Raw `Console.Write` kept for the live
token stream (zero latency impact). `AnsiConsole` used for all structural elements:
tool start/end markers (grey dimmed), turn separators (grey rule), warnings (yellow),
errors (red). `OnToolStarted` and `OnToolCompleted` wired on `TurnCallbacks`.
All model-controlled strings (`Markup.Escape`d before rendering). Spec and
implementation notes in `docs/decisions/cli-output-formatting/`.

**Upgrade path — stateful renderer (future):** When in-place spinner → checkmark
updates or a persistent footer/status bar are needed, `CliRenderer` graduates from
a static class to a stateful object with a `Start()` / `Stop()` lifecycle. It will
own the terminal via `AnsiConsole.Live()` or direct ANSI cursor control, tracking
the current content row and any in-flight tool lines. The `TurnCallbacks` delegate
signatures stay identical — the upgrade is internal to `CliRenderer`. `Program.cs`
will create one instance and pass it down rather than calling static methods. Do not
build this until the static renderer is visibly insufficient — the seam is already right.

### ~~Bash approval — command allowlist + `--accepts-commands` flag~~ ✓ done — PR #29
`InteractiveApprovalGate` accepts an optional set of trusted executable prefixes at
construction time. When a `bash` call's first token matches an entry, it is silently
approved without prompting. `--accepts-commands <dotnet,git,...>` populates the set
at startup; omitting the flag leaves all `Confirm`-tier bash calls requiring explicit
`yes`. The allowlist check fires before `PromptAsync` in all interactive modes;
`AllowAll` is unaffected. Spec and implementation in
`docs/decisions/bash-command-allowlist/`.

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

### ~~Tiered approval friction~~ ✓ done — PR #22
`ApprovalTier` on `ITool` (`Auto` / `Prompt` / `Confirm`) declares per-tool approval requirements. `IApprovalGate` is consulted by `AgentLoop` before every dispatch. `SessionMode` (`Default` / `AcceptsEdits` / `AllowAll`) controls gate behaviour; `--accepts-edits` and `--allow-all` flags set the mode at startup. Denied calls are recorded as `ToolCallStatus.Denied`. Spec and implementation in `docs/decisions/tiered-approval-friction/`.

### ~~Codebase audit & hardening~~ ✓ done — PR #23
Full hypothesis-driven audit against four pillars: high-performance .NET 10, context engineering, vendor independence, and extensibility. Seven findings resolved:

- **C-1 (critical):** Path containment in `ReadFileTool`, `WriteFileTool`, and `BashTool` replaced `StartsWith` with `Path.GetRelativePath`-based `IsWithinRoot` guard — closes directory-boundary false positive where sibling paths (e.g. `/root-evil`) passed a `/root` prefix check.
- **ANSI spoofing hardening:** `ControlCharSanitizer` added; wired into `InteractiveApprovalGate` so model-controlled tool names and call summaries have all C0/C1 control characters and ANSI escape sequences replaced with visible literals before being printed for user approval.
- **W-2:** `SqliteCommand` disposal fixed across all four command sites in `SqliteSessionStore`; `tool_outputs` insert command now created once per `AppendAsync` call with parameters rebound per iteration rather than reallocated.
- **O-1:** `AgentLoop` message list uses C# 14 spread `[.. conversationHistory, msg]` for exact-capacity allocation; `toolResults` preallocated from `Contents.Count`.
- **O-2:** `TokenThresholdPolicy.ShouldCompact` replaced double LINQ chain with zero-allocation `foreach` loops.
- **O-3:** Streaming retry buffer in `RetryingChatClient` preallocated at capacity 128.
- **O-4:** `UsageSummary` promoted from `record class` to `readonly record struct`.

Full audit report, design decisions, and confirmed-correct inventory in `docs/audit-and-hardening-2026-06-07.md`.

### ~~Rich TUI — foundation~~ ✓ done — PR #24
Session orchestration extracted from `Phelix.Cli/Program.cs` into `PhelixSession` in
`Phelix.Core`. `TurnCallbacks` introduced as a per-turn `readonly record struct` with
`OnChunk`, `OnToolStarted`, and `OnToolCompleted` delegates. `TurnResult` discriminated
union replaces throwing across the session boundary. `TurnExitReason.Error` added.
Both `Phelix.Cli` and the upcoming `Phelix.Tui` drive the same `PhelixSession` —
no session logic duplication. Spec and implementation in `docs/decisions/rich-tui/`.

### ~~Rich TUI — rendering layer~~ ✓ done — PR #25
All five rendering-layer pieces are built and compiling clean. `IApprovalGate` signature
extended with an `args` parameter so `TuiApprovalGate` can render a structured argument
grid in the approval panel. All 116 existing tests pass. Spec in
`docs/decisions/rich-tui-rendering/`.

### ~~Rich TUI — entry point wiring~~ ✓ done — PR #26
TUI is now the default invocation. `phelix` starts the TUI; `phelix --cli` drops to the
terminal REPL; `phelix --cli "prompt"` runs a single turn and exits. Spec and
implementation notes in `docs/decisions/tui-entry-point/`.

**What was built:**
- `HostMode.cs` — discriminated union (`HostMode.Tui` / `HostMode.Cli`) replaces the
  `SessionMode` parameter on `PhelixHost.Build`
- `PhelixHost.cs` — `BuildApprovalGate` switches on `HostMode` once; `Build` returns
  `TuiState? InitialState` (non-null for `HostMode.Tui`) populated from config metadata
- `TuiSession.cs` — constructor now accepts `Channel<TuiEvent>` created by `Program.cs`,
  resolving the gate/session sequencing problem without coupling either to the other
- `Program.cs` — rewritten with preview-4 `System.CommandLine` API; TUI default, `--cli`
  opt-in; `--accepts-edits` and `--allow-all` are CLI-only flags

### ~~Rich TUI — removed~~ reverted by design decision
TUI removed in favour of a polished CLI. `Phelix.Tui` project deleted in full.
`HostMode`, `TuiSession`, `TuiRenderer`, `TuiState`, `TuiEvent`, `TuiApprovalGate`,
and `TerminalRenderer` are gone. `PhelixHost` simplified to accept `SessionMode`
directly; `CliRenderer` replaces the subset of `TerminalRenderer` the CLI needed.
`phelix` is now the CLI directly — no `--cli` flag required. All 116 Core tests
pass; zero warnings on build.

### ~~Markdown block rendering~~ ✓ done
`Markdig` added to `Phelix.Cli`. `StreamingMarkdownWriter` buffers streamed chunks
per text segment (between tool calls, and the final answer) and flushes through
`MarkdownBlockRenderer` — a stateless dispatcher that walks the parsed block tree
and renders headings, paragraphs, lists (nested, ordered/unordered), tables, fenced
code, and thematic breaks via Spectre. All literal text passes through
`Markup.Escape`. Non-TTY output bypasses rendering entirely and streams raw
markdown through. New `tests/Phelix.Cli.Tests` project (`Spectre.Console.Testing`)
covers rendering and escaping. Spec in `docs/decisions/markdown-block-rendering/`.

### ~~CliRenderer chrome accent pass~~ ✓ done — PR #34
`CliRenderer` chrome brought in line with the indigo accent (`#a78bfa`) established
by `MarkdownBlockRenderer`. Tool-start `◆` and tool-name, tool-complete `✓` and
tool-name, turn-separator rule, REPL `>` prompt, and "Phelix" greeting wordmark all
use the accent. Arg lists and duration metadata stay `grey dim` for visual hierarchy.
`✗` and warnings stay red/yellow — semantic urgency preserved. Spec and implementation
notes in `docs/decisions/cli-chrome-accents/`.

### ~~OpenTelemetry tracing~~ ✓ done
`PhelixTelemetry` exposes a single `ActivitySource` with all span and tag name
constants. Every agent turn and tool call emits a structured span; wired through
`AgentLoop`, `PhelixHost`, and `Program.cs`. Verified end-to-end against a local
Jaeger instance. Spec and implementation notes in `docs/decisions/opentelemetry/`.

### ~~Persistent allowed commands~~ ✓ done — PR #35
Trusted bash command prefixes can now be persisted in config (`allowed_commands` on
`PhelixConfig`, surfaced via `FileConfigProvider`) instead of only via the per-session
`--accepts-commands` flag. Supersedes the flag-only model from the bash-command-allowlist
decision for the single-developer-on-their-own-machine case. Same fix also corrects the
`max-turns` default to use `ModelConfig.DefaultMaxTurns`. Spec in
`docs/decisions/persistent-allowed-commands/`.

### ~~`list_files` recursion depth~~ ✓ done — PR #38
`ResolveGlob` selects `SearchOption.AllDirectories` when the normalized glob contains
`**`, otherwise `SearchOption.TopDirectoryOnly` — recursion now follows the pattern's
intent rather than always recursing. No new tool parameter. Spec and implementation
notes in `docs/decisions/list-files-recursion-depth/`.

### ~~Session info display~~ ✓ done — PR #39
Startup banner, per-turn footer, and `/status` surface the active model name/id,
provider, session mode, and live token usage. `SessionInfo` is a CLI-layer presentation
carrier rendered via shared `CliRenderer` helpers — no changes to `PhelixSession`,
`AgentLoop`, `Turn`, or `UsageSummary`. Spec and implementation notes in
`docs/decisions/session-info-display/`.

---

## Phase 3 — Adoption & Extensibility
*The shift from "well-engineered personal tool" to "harness other developers can
adopt and shape." Items are ordered for implementation — each builds on the one
before. Start a spec in `docs/decisions/<feature>/` before touching code. This phase
absorbs the backlog items that belong to the adoption/extensibility arc; pure R&D
items (BM25, semantic search, knowledge graph) stay in the Backlog, and multi-agent
patterns stay in Vision.*

**Why this ordering:** the first three items are low-cost, high-visibility adoption
unblockers that reuse infrastructure already in place. The middle block (skills,
dynamic tool loading, hooks, MCP) is the extensibility core that decides whether
other people can shape Phelix without forking it. The tail (sensors, eval, sub-agents,
memory) is depth that only pays off once the surface area above it exists.

### 3.1 — Session resume / continue
**The single most-expected missing feature.** Everything needed is already persisted
to SQLite (`SqliteSessionStore`), but `PhelixSession` is created fresh per invocation
and nothing replays it.
- `phelix --continue` resumes the most recent session; `phelix --resume <id>` resumes
  a named session by id. Rehydrate `conversationHistory` from the `turns` table.
- Depends on the audit's **S-2** note: make `PhelixSession.ConversationHistory` an
  `ImmutableArray<ChatMessage>` over the mutable `List` first — this is the enabler
  for both resume and a later fork capability.
- Wiring + a list/picker, not new infrastructure. Highest leverage in the phase.

### 3.2 — Global tool packaging — `dotnet tool install -g phelix`
*(moved up from Backlog — it's the first friction a new adopter hits.)*
No install path exists yet; the harness runs only via `dotnet run --project`. The
README already promises a global tool as the release mechanism. The app is already
relocatable (config, session logs, and `AGENTS.md` resolve from `~/.phelix/` and the
invoker's CWD — no repo-root assumptions), so packaging is metadata, not a refactor.
- Distribution decision: **.NET global tool** (audience is .NET devs who have the
  runtime); not self-contained or AOT for the first cut.
- `Phelix.Cli.csproj`: add `PackAsTool`, `ToolCommandName=phelix`, `PackageId=phelix`,
  `AssemblyName=phelix`, plus NuGet metadata (version, authors, description, license,
  repo URL).
- Validate locally: `dotnet pack -c Release` → `dotnet tool install -g --add-source
  ./nupkg phelix`, confirm `phelix` and `phelix "<prompt>"` work from an unrelated
  directory.
- Later: tag-triggered CI job to pack + push to NuGet (needs a NuGet API key secret).
- Does **not** require Native AOT. AOT remains a separate future item — it is blocked
  by YamlDotNet's reflection-based config parser (`FileConfigProvider`) and would need
  an AOT-safe parser first.

### 3.3 — Custom slash commands
Only `/status` exists today, hardcoded. User-defined commands are how a developer
teaches the harness *their* workflow without touching core code.
- Markdown prompt templates discovered from `~/.phelix/commands/*.md` (global) and
  per-repo `.phelix/commands/*.md`; filename becomes the command (`review.md` → `/review`).
- Argument substitution (e.g. `$ARGUMENTS` / positional `$1`) so commands take input.
- Slash-command dispatch lives in the CLI input loop; resolves a template to a normal
  user turn. Cheap to build, high perceived polish.

### 3.4 — Skills system
*(moved from Backlog — the highest-leverage extensibility feature.)* Today a skill is
a static markdown file (`skills/git-workflow/SKILL.md`) that nothing auto-loads.
- Skill discovery from `~/.phelix/skills/` (global) **and** per-repo `.phelix/skills/`;
  each skill is a directory with a `SKILL.md` (name + description frontmatter + body).
- Inject a cheap name+description catalog into context; a `load_skill` tool pulls the
  full body on demand (do not preload every skill body).
- Evaluate each skill empirically with and without — stale or redundant skills hurt
  performance (see 3.9, Evaluation discipline).
- Reference: Cursor replaced 15,000 lines of orchestration with a 200-line skill file.
- **Sequenced with 3.5** — the on-demand catalog pattern is shared.

### 3.5 — Dynamic tool loading
*(moved from Backlog — most spec-ready item; pairs with Skills.)* All tool schemas are
registered at startup and re-sent every turn regardless of use. At ~160 tokens per
tool this is a fixed floor that compounds with every tool added.
- Replace the startup registry with a lightweight catalog (tool name + one-liner); the
  agent loads full schemas on demand when it decides it needs a tool.
- Same catalog-then-load shape as 3.4 — build the two together so the mechanism is
  shared, not duplicated.
- Spec written in `docs/decisions/dynamic-tool-loading/spec.md`; no implementation yet.
- Architectural change with session and approval-gate implications.

### 3.6 — Hooks / lifecycle events
The extension primitive that unlocks the most for the least core surface area. Lets a
team enforce behavior *deterministically* instead of trusting the model to remember.
- Lifecycle events: `SessionStart`, `PreToolUse`, `PostToolUse`, `Stop` (more later).
- User-configured commands run on each event (config in `config.yaml` or `.phelix/`);
  `PreToolUse` can block or rewrite a dispatch, `PostToolUse` observes results.
- **Folds in the backlogged secret scrubber:** a `PostToolUse`/logging-stage hook that
  pattern-matches secret shapes (API keys, tokens, `Bearer ...`) and blanks them before
  content reaches the session store. Must not mutate content sent back to the model —
  scrubbing is a logging concern only.

### 3.7 — MCP client support
Reconsiders the prior "not in scope" call. MCP is now the de facto way harnesses gain
tools (GitHub, Postgres, Playwright, internal company servers) without the author
writing each integration. For a .NET-native harness, "speaks MCP *and* has first-class
Roslyn" is a genuine differentiator.
- MCP client that connects to configured servers (stdio + HTTP) and surfaces their
  tools through the existing `ITool` / `ToolRegistry` seam.
- Reuses the approval-tier and path-containment machinery; external tools default to
  `Confirm` tier.
- Benefits directly from 3.5 (dynamic loading) so external tool schemas don't bloat
  every turn.

### 3.8 — Plan mode (in core)
Reconsiders "build as a skill." A read-only planning phase that gates writes behind an
explicit approval is an expected safety/UX affordance — and a skill can't enforce
read-only at the dispatch layer the way the core can.
- New `SessionMode.Plan` alongside `Default` / `AcceptsEdits` / `AllowAll`; in this
  mode the approval gate denies every non-read tier at dispatch.
- `--plan` flag and an in-session toggle; exiting plan mode requires explicit user
  confirmation before any write executes.
- Leans entirely on the approval-tier + session-mode machinery already in place.
- Supersedes the backlogged "Structured loop: Plan → Execute → Verify" prompt-only
  idea, which can ride along as a system-prompt nudge once the mode exists.

### 3.9 — Sensors: build & diagnostics feedback loop
*(was the standalone "Phase 3 — Sensors"; now sequenced here.)* Phelix's signature
.NET differentiator: close the loop so build/test/diagnostic results feed back
automatically instead of the agent re-reading files to guess what broke.
- Run `dotnet build` / `dotnet test` and surface structured results as `TurnEvent`s
  (the `TurnEvent` hierarchy was already reserved for this in the session schema).
- A `roslyn_diagnostics` tool / sensor that returns analyzer + compiler diagnostics
  for changed files.
- Feed sensor output back into the next turn as feedforward context.

### 3.10 — Evaluation discipline
*(moved from Backlog — gates safe tuning of everything above.)* No systematic way to
compare harness performance with vs. without a skill, prompt change, or tool.
- A/B harness that replays a fixed task set and reports outcome deltas.
- Needed before the harness matures beyond personal use — and before the skill library
  (3.4) grows large enough that regressions hide.

### 3.11 — Sub-agents / task delegation
Deferred but not designed against. Once skills land, "spawn a focused sub-agent with
its own context for this search/review" is the next power-user ask.
- A sub-agent is "another `PhelixSession` with a tool handle," not a rewrite — keep the
  `PhelixSession` boundary clean so this stays additive.
- Serial first (delegate → return summary); parallelism and the Reflection/Critic and
  Orchestrator/Worker/Validator patterns in Vision build on top of this.

### 3.12 — Durable cross-session memory
*(moved from Backlog — correctly last; depends on the skills layer.)* No cross-session
memory exists today.
- A project memory file the agent reads at session start and writes back to (facts,
  decisions, conventions) — distinct from per-session SQLite history.
- Pairs with the backlogged Conventions / Rules files idea; design the two schemas
  together.

---

## Backlog
*Good ideas that need a spec and the right moment. Not yet actionable. Adoption and
extensibility items have moved to Phase 3; what remains here is per-feature polish and
deeper R&D that is not on the adoption critical path.*

### Conventions / Rules files with examples
No mechanism for per-project behavioral anchors beyond `AGENTS.md`. Other harnesses use richer rule files with worked examples to constrain agent behavior without relying solely on system prompt tuning.
- `~/.phelix/rules/` or per-repo `.phelix/rules.md` with named conventions and examples
- Evaluate how Cursor, Gemini, and other harnesses handle this before designing the schema
- Design alongside **Phase 3.12** (durable memory) — overlapping schema concerns.

### Agent-facing exception and validation messages
Exceptions and validation errors are written for human developers. An agent reading them wastes tokens disambiguating intent.
- Custom exception types with structured, unambiguous messages the agent can act on directly
- Validation errors carry the exact field, constraint violated, and suggested fix — no prose guessing required

### ~~Session log naming~~ ✓ done
`SessionContext` record (`SessionId`, `SessionName?`, `StartedAt`) replaces the static
`SessionLogger.SessionId`. At startup in interactive mode, the user is prompted for an
optional session name; single-turn invocations skip the prompt. The sanitized name is
baked into both the `.jsonl` and `.db` filenames via `SessionContext.FileSlug`
(`yyyy-MM-dd-<name>-<sessionId>` or `yyyy-MM-dd-<sessionId>` when unnamed) and stored
as a nullable `session_name` column on every `turns` row. Name is immutable for the
session lifetime. Spec and implementation in `docs/decisions/session-log-naming/`.

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
