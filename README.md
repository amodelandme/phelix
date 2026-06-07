# Phelix - A .Net Agent Harness

[![CI](https://github.com/amodelandme/phelix/actions/workflows/ci.yml/badge.svg)](https://github.com/amodelandme/phelix/actions/workflows/ci.yml) [![Last commit](https://img.shields.io/github/last-commit/amodelandme/phelix)](https://github.com/amodelandme/phelix/commits/main) ![.NET 10](https://img.shields.io/badge/.NET-10-512BD4) ![License](https://img.shields.io/badge/license-MIT-green)

> **Currently building:** context compaction · retry / circuit breaker

---

A terminal-based AI coding harness for .NET developers. Phelix wraps a language model in a deterministic loop — reading files, writing code, running builds, and feeding results back into the next turn.

```bash
phelix "add OpenTelemetry tracing to the OrdersService"
```

---

## How it works

```
CLI prompt
    └─ AgentLoop
          ├─ model call (IChatClient)
          ├─ tool dispatch  ←─ read files · write files · bash · search
          └─ session log    ←─ full turn record written after every turn
```

The loop runs until the model stops calling tools or a turn limit is hit. Every turn — tool calls, token usage, exit reason — is written to a JSONL session log at `~/.phelix/sessions/`.

---

## Configuration

Provider and model profiles live in `~/.phelix/config.yaml`:

```yaml
providers:
  openrouter:
    endpoint: https://openrouter.ai/api/v1
    apiKeyEnvVar: OPENROUTER_API_KEY

models:
  default:
    provider: openrouter
    modelId: anthropic/claude-sonnet-4-6
  fast:
    provider: openrouter
    modelId: qwen/qwen3.5-flash-20260224
```

Project guidance lives in `AGENTS.md` at the repo root — committed alongside the code, not stored in a cloud account.

---

## Context window design

One of the core engineering problems in an agent harness is keeping the context window under control. Left unchecked, every tool result gets re-sent to the model on every subsequent turn — costs compound fast and the model starts losing focus on older material.

We adopted two strategies:

**1. Ephemeral tool pattern.** After a turn completes, raw tool call and tool result messages are stripped from the history passed to the next turn. The model already synthesized the tool output into its final reply — re-sending the raw bytes wastes tokens without adding information. If the model needs the data again, it calls the tool again.

We chose this over rolling summarization (asking a cheap model to compress older turns into a paragraph) because it requires no extra API call, loses nothing semantically, and is trivially reversible. Summarization is still on the roadmap for longer sessions where even the assistant replies accumulate.

**2. Per-result truncation.** Every tool result is capped at 2,000 characters using a head/tail split — the first 80% and last 20% are kept, the middle is replaced with a `[X chars truncated]` notice. This protects the current turn from a single runaway result (a large file read, a verbose bash output) before the ephemeral pattern can clean it up at turn end.

The full tool output is never discarded — it is written verbatim to the JSONL session log at `~/.phelix/sessions/`. If the model needs to inspect a result it no longer has in context, it can read its own session log via `read_file`.

---

## Status

| Phase | Goal | Status |
|---|---|---|
| 1 — Skeleton | streaming output, turn loop | done |
| 2 — Tools | file read/write, bash, search, session log | in progress |
| 3 — Sensors | Roslyn and build feedback close the loop | upcoming |
| 4 — Release | `dotnet tool install -g phelix` | upcoming |

---

## Tech stack

| | |
|---|---|
| Language | C# 14 / .NET 10 |
| Model abstraction | `Microsoft.Extensions.AI` (`IChatClient`) |
| TUI | Spectre.Console |
| CLI | System.CommandLine |

---

## License

MIT
