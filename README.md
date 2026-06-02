# Phelix

> A terminal-based AI coding harness for .NET developers.

---

## What is Phelix?

A control system that wraps a language model in a deterministic loop — reading your codebase, writing code, running builds, and feeding compiler results back into the next turn, all from your terminal.

```bash
phelix "add OpenTelemetry tracing to the OrdersService"
```

---

## Why Phelix

Phelix runs inside the compiler.

Roslyn gives it a live semantic model of your solution — symbol resolution, diagnostics, syntax tree analysis. The same APIs Visual Studio uses, feeding directly into the agent loop.

---

## Design Philosophy

**Primitives over features.** Phelix ships with the minimum viable control loop. Things like sub-agents, plan mode, and code review are built as skills or left to the user. The core stays small and easy to reason about.

**Configured in the repository.** Project guidance lives in a `PHELIX.md` file you write and commit. Skills are Markdown files. Tools are C# classes. Nothing lives in a cloud account.

```markdown
# PHELIX.md — OrdersService

## Project
ASP.NET Core 10 Web API. Clean Architecture. PostgreSQL via EF Core.

## Skills
- dotnet-webapi
- efcore
- opentelemetry

## Constraints
- Never modify files under /legacy
- Always run `dotnet build` after changes to *.cs files
```

**Model-agnostic.** Every provider is accessed through `Microsoft.Extensions.AI`'s `IChatClient`. Switching models is a one-line config change.

---

## Architecture

```
┌─────────────────────────────────────────────────┐
│                  CLI Entry Point                │
│           Phelix.Cli / phelix (command)         │
└────────────────────┬────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────┐
│                  TUI Layer                      │
│        Phelix.Tui (Spectre.Console)             │
│   streaming output · status line · spinner      │
└────────────────────┬────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────┐
│               Orchestration Loop                │
│              Phelix.Core.Agent                  │
│  turn loop · tool dispatch · sensor invocation  │
└──────┬──────────────────────────────┬───────────┘
       │                              │
┌──────▼──────┐              ┌────────▼───────────┐
│   Model     │              │   Tool Registry    │
│   Adapter   │              │  Phelix.Core.Tools │
│ (IChatClient)│             │  built-in + custom │
└─────────────┘              └────────────────────┘
       │
┌──────▼──────────────────────────────────────────┐
│            Sensor Pipeline                      │
│         Phelix.Core.Sensors                     │
│     Roslyn · build · test · custom              │
└─────────────────────────────────────────────────┘
```

The model acts, sensors fire, feedback enters the next turn. That cycle is what makes Phelix a harness.

---

## Status

Phelix is in active development. Current phase: **Phase 2 — Tools**.

| Phase | Goal | Status |
|---|---|---|
| 1 — Skeleton | `phelix "hello"` runs, streams output | done |
| 2 — Tools | Agent reads files, calls tools, dispatches results | in progress |
| 3 — Sensors | Roslyn and build feedback close the loop | upcoming |
| 4 — Context | `PHELIX.md` and skills work end-to-end | upcoming |
| 5 — Release | `dotnet tool install -g phelix` | upcoming |

---

## Tech Stack

| Layer | Choice |
|---|---|
| Language | C# 14 |
| Runtime | .NET 10 |
| Model abstraction | `Microsoft.Extensions.AI` (`IChatClient`) |
| TUI | Spectre.Console |
| CLI | System.CommandLine |
| Semantic analysis | Roslyn (`Microsoft.CodeAnalysis`) |
| Distribution | `dotnet tool install -g phelix` |

---

## License

MIT
