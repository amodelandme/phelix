# Phelix — A .NET Agent Harness

[![CI](https://github.com/amodelandme/phelix/actions/workflows/ci.yml/badge.svg)](https://github.com/amodelandme/phelix/actions/workflows/ci.yml) [![Last commit](https://img.shields.io/github/last-commit/amodelandme/phelix)](https://github.com/amodelandme/phelix/commits/dev) ![.NET 10](https://img.shields.io/badge/.NET-10-512BD4) ![License](https://img.shields.io/badge/license-MIT-green)

> **Currently building:** skills system · secret-scrubber middleware

---

A terminal-based AI coding harness for .NET developers. Phelix wraps a language model in a deterministic loop — reading files, writing code, running builds, and feeding the results back into the next turn. Configuration lives in your repo, not a cloud account; the model is swappable; the core stays small and auditable.

```bash
phelix "add OpenTelemetry tracing to the OrdersService"
```

The guiding one-liner:

```
Agent = Model + Harness
Phelix = the Harness, built for .NET
```

---

## How it works

```
CLI prompt
    └─ PhelixSession ─ AgentLoop
          ├─ model call (IChatClient, streamed)
          ├─ approval gate   ←─ Auto · Prompt · Confirm, per tool
          ├─ tool dispatch   ←─ read · write · bash · search
          └─ session record  ←─ written to JSONL + SQLite after every turn
```

The loop runs until the model stops calling tools or a turn limit is hit. Every turn — tool calls, token usage, exit reason — is persisted in real time (see [Sessions](#sessions--logging)).

---

## Install & run

Phelix is not yet packaged as a global tool — that lands in the release phase. For now, run it from source.

**Prerequisites:** .NET 10 SDK, and an API key for your provider (OpenRouter by default).

```bash
git clone https://github.com/amodelandme/phelix.git
cd phelix
export OPENROUTER_API_KEY=sk-...

# single-turn: run one task and exit
dotnet run --project src/Phelix.Cli -- "list the public methods on AgentLoop"

# interactive: REPL (prompts for an optional session name, type 'exit' to quit)
dotnet run --project src/Phelix.Cli
```

### Approval flags

By default every `Prompt`- and `Confirm`-tier tool call asks before running (see [Approvals & safety](#approvals--safety)). Loosen that per session:

| Flag | Effect |
|---|---|
| `--accepts-edits` | Auto-approve `Prompt`-tier calls (file writes). |
| `--allow-all` | Auto-approve every tool call. |
| `--accepts-commands dotnet,git` | Auto-approve `bash` calls whose first token matches the list; everything else still confirms. |

```bash
dotnet run --project src/Phelix.Cli -- --accepts-edits --accepts-commands dotnet,git "fix the failing test in AgentLoopTests"
```

---

## Configuration

Provider and model profiles live in `~/.phelix/config.yaml` (override the path with the `PHELIX_CONFIG` environment variable). When the file is absent, a built-in default targeting OpenRouter is used without error.

```yaml
active_model: sonnet

system_prompt: "You are Phelix, a .NET coding agent. Prefer tools over prose."

providers:
  openrouter:
    base_url: https://openrouter.ai/api/v1
    api_key_env: OPENROUTER_API_KEY

models:
  sonnet:
    provider: openrouter
    model_id: anthropic/claude-sonnet-4-6
    max_turns: 10
  fast:
    provider: openrouter
    model_id: qwen/qwen3.5-flash
    max_turns: 5

# optional — global retry policy, overridable per-model
retry:
  max_attempts: 4
```

`active_model` must match a key in `models`; each model's `provider` must match a key in `providers`. **API keys are never stored in config** — only the name of the environment variable that holds the key. Swapping providers (Claude → GPT → a local model) is a config change, not a code change, because every model is accessed through `Microsoft.Extensions.AI`'s `IChatClient`.

Project-specific guidance lives in **`AGENTS.md`** at the repo root — committed alongside the code. Phelix reads it on startup and composes it with the base system prompt.

---

## Tools

The model acts on your codebase through six built-in tools. Each declares an approval tier (see below).

| Tool | Tier | What it does |
|---|---|---|
| `read_file` | Auto | Read a file from disk. |
| `write_file` | Prompt | Create or overwrite a file. |
| `bash` | Confirm | Run a shell command (allowlist can downgrade to Auto). |
| `list_files` | Auto | List files matching a glob (skips `.git`, `bin`, `obj`). |
| `search_code` | Auto | Text/regex search across the codebase. |
| `search_session` | Auto | FTS5 query over tool outputs from earlier in the session. |

---

## Approvals & safety

Every tool declares an **approval tier**, and the agent loop consults an approval gate before each dispatch:

- **Auto** — runs without asking (reads, searches).
- **Prompt** — asks before running (file writes).
- **Confirm** — requires explicit confirmation (shell commands).

Session modes set at startup (`Default` / `AcceptsEdits` / `AllowAll`) shift the whole gate; the `--accepts-commands` allowlist downgrades matching `bash` calls to Auto. Denied calls are recorded as `Denied` in the session log rather than silently dropped.

Two more guards run underneath:

- **Path containment** — `read_file`, `write_file`, and `bash` resolve paths against the working root via `Path.GetRelativePath`, so sibling-prefix paths (e.g. `/root-evil` against `/root`) can't escape the boundary.
- **Control-char sanitizing** — model-controlled tool names and call summaries have C0/C1 control characters and ANSI escapes replaced with visible literals before they're printed for approval, so a tool call can't spoof the terminal.

---

## Context window design

Keeping the context window under control is the core engineering problem of an agent harness. Left unchecked, every tool result is re-sent on every subsequent turn — costs compound and the model loses focus on older material. Phelix uses three layers:

**1. Ephemeral tool pattern.** After a turn completes, raw tool-call and tool-result messages are stripped from the history passed to the next turn. The model already synthesized the output into its reply — re-sending the raw bytes wastes tokens. If it needs the data again, it calls the tool again. This was chosen over rolling summarization for the common case because it needs no extra API call, loses nothing semantically, and is trivially reversible.

**2. Per-result truncation.** Every tool result is capped at 2,000 characters using an 80/20 head/tail split — the first 80% and last 20% are kept, the middle replaced with a `[X chars truncated]` notice. This protects the current turn from a single runaway result before the ephemeral pattern cleans it up at turn end. The full output is never lost — it's written verbatim to the session store.

**3. Compaction for long sessions.** When the estimated token count of the live history crosses a threshold (default 40,000), the history is replaced with a single model-generated summary, reconstructed from the durable SQLite store. After compaction the model can still reach earlier detail on demand via the `search_session` tool, which queries the FTS5-indexed tool outputs.

---

## Sessions & logging

Every turn is persisted in real time to two complementary stores under `~/.phelix/sessions/`, sharing one base name (`yyyy-MM-dd-<name>-<sessionId>`):

- **JSONL log** — append-only, human-readable, one record per turn. The audit trail; survives crashes without corruption.
- **SQLite + FTS5** — queryable and full-text indexed. Backs compaction and the `search_session` tool.

Each record captures the tool calls, token usage, exit reason, and timestamps for the turn. Model calls themselves run through `RetryingChatClient` middleware — exponential backoff with jitter on 429/5xx/timeout/IO errors, with streaming buffered per attempt so a retry never emits partial output.

---

## Project layout

```
phelix/
├── src/
│   ├── Phelix.Cli/      # entry point — REPL, renderer, host wiring
│   └── Phelix.Core/     # all logic, no UI deps
│       ├── Agent/       # AgentLoop, approval gates, retry, callbacks
│       ├── Config/      # YAML config, provider/model profiles
│       ├── Context/     # AGENTS.md loading
│       ├── Session/     # PhelixSession, JSONL + SQLite stores, compaction
│       ├── Telemetry/   # OpenTelemetry ActivitySource
│       └── Tools/       # the six built-in tools + registry
├── tests/Phelix.Core.Tests/   # unit + integration tests (no real model/fs/terminal)
├── docs/                # ARCHITECTURE.md · ROADMAP.md · decisions/
├── skills/              # markdown skill files
└── AGENTS.md            # instructions for AI working on Phelix itself
```

Full design rationale is in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md); per-feature specs and implementation notes live in `docs/decisions/`.

### Building & testing

```bash
dotnet build           # zero warnings expected
dotnet test            # 112 tests, no network or filesystem dependencies
```

---

## Status

| Phase | Goal | Status |
|---|---|---|
| 1 — Skeleton | streaming output, turn loop | ✅ done |
| 2 — Tools | file read/write, bash, search, session log | ✅ done |
| — Hardening & context engineering | compaction, SQLite store, retry middleware, tiered approvals, security audit | ✅ done |
| — Skills & secret scrubber | on-demand skill loading; redact secrets before they hit the log | 🔨 in progress |
| 3 — Sensors | Roslyn diagnostics + build feedback close the loop automatically | ⏳ planned |
| 4 — Release | `dotnet tool install -g phelix` | ⏳ planned |

A fair amount of hardening and context-engineering work landed out of phase order — that's why compaction, retry, and the approval system are done while the Phase 3 sensor loop is still ahead. The full backlog and vision are in [`docs/ROADMAP.md`](docs/ROADMAP.md).

---

## Tech stack

| | |
|---|---|
| Language / runtime | C# 14 / .NET 10 |
| Model abstraction | `Microsoft.Extensions.AI` (`IChatClient`) |
| CLI parsing | System.CommandLine |
| Terminal rendering | Spectre.Console (structural output; raw stream for token chunks) |
| Session store | SQLite + FTS5 |
| Observability | OpenTelemetry |

---

## License

MIT
</content>
</invoke>