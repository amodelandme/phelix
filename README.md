# Phelix

> A terminal-based AI coding harness built natively for the .NET ecosystem.

---

## What is Phelix?

Phelix is not a chat wrapper. It is not a library you add to your application.

It is a **control system** — a harness that wraps a language model in a deterministic loop capable of reading your codebase, writing code, running builds, and receiving compiler feedback, all without leaving your terminal.

```bash
phelix "add OpenTelemetry tracing to the OrdersService"
```

The model works. Phelix governs.

---

## The Problem With Other Harnesses

Most AI coding tools are built by web developers, for web developers, in TypeScript or Python. They treat .NET as a target — something the model can write code *for*, not something the harness can reason *with*.

That gap matters.

When a TypeScript harness runs against a C# codebase, it sees text files. It can read them, write them, and shell out to `dotnet build` and scrape the output. That is the ceiling.

Phelix's ceiling is higher — because Phelix is built in .NET and has access to the same APIs the compiler uses.

---

## The .NET Moat — Roslyn

[Roslyn](https://github.com/dotnet/roslyn) is the C# compiler exposed as a library. It gives Phelix a full semantic model of your codebase — not text search, but **symbol resolution**.

What that means in practice:

| Capability | TypeScript harness | Phelix |
|---|---|---|
| Read and write files | yes | yes |
| Find all usages of a type across the solution | grep | Roslyn symbol lookup |
| Compiler errors after a file write | scrape `dotnet build` output | Roslyn diagnostics in-process, instantly |
| Detect architectural violations | string pattern matching | structural syntax tree analysis |
| Code metrics (complexity, coupling) | external tool | Roslyn workspace API |

Compiler feedback closes the loop. When the model writes a file, Phelix does not wait for the developer to notice the build is broken — it feeds the diagnostics back into the next turn automatically. The model corrects itself.

This is not possible to replicate in a harness that lives outside the .NET runtime.

---

## Design Philosophy

### Primitives, not features

Phelix ships with the minimum viable control loop. Features that other tools bake in — sub-agents, plan mode, code review agents — are built by the user as **skills** or left out entirely. The core stays small and auditable.

### .NET-native, not .NET-compatible

The `dotnet` CLI is a built-in tool. Roslyn is a first-class citizen. NuGet metadata is readable context. Phelix is a member of the .NET toolchain, not a visitor.

### The harness bends to the codebase

Configuration lives in the repository, not in a cloud account. Project-specific guidance lives in a `PHELIX.md` file you write and commit. Skills are Markdown files. Tools are C# classes you can extend.

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
- Always run `dotnet build` after any change to *.cs files
```

### Model-agnostic by design

Phelix never calls a model SDK directly. Every provider is accessed through `Microsoft.Extensions.AI`'s `IChatClient` abstraction. Swapping from Claude to GPT-4o is a one-line config change.

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

The orchestration loop is the heart of Phelix. The model acts, sensors fire, feedback enters the next turn. That cycle — sense, act, sense — is what makes Phelix a harness rather than a wrapper.

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

## What Phelix Does Not Do

This list is as important as what it does.

- No sub-agents in core — build them as skills
- No plan mode in core — build it as a skill
- No cloud account or sync — all state is local
- No GUI — terminal only
- No autonomous PR creation — explicit human action

---

## License

MIT
