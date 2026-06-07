# Phelix - A .Net Agent Harness

[![CI](https://github.com/amodelandme/phelix/actions/workflows/ci.yml/badge.svg)](https://github.com/amodelandme/phelix/actions/workflows/ci.yml) [![Last commit](https://img.shields.io/github/last-commit/amodelandme/phelix)](https://github.com/amodelandme/phelix/commits/main) ![.NET 10](https://img.shields.io/badge/.NET-10-512BD4) ![License](https://img.shields.io/badge/license-MIT-green)

> **Currently building:** tool output truncation · context compaction

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
