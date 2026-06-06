# Phelix — Architecture Document

> **"There are many agent harnesses, but this one is yours."**
> A minimal CLI coding harness for .NET developers.

**Version:** 0.1 (pre-implementation)
**Date:** May 2026
**Status:** Design — not yet implemented

---

## 1. What Phelix Is

Phelix is a terminal-based AI coding harness built natively for the .NET ecosystem.
It wraps a language model in a deterministic control system — handling context, tools,
feedback, and safety — so the model can do reliable work inside a .NET codebase.

The one-liner that governs every design decision:

```
Agent = Model + Harness
Phelix = the Harness, built for .NET
```

Phelix is **not** a framework. It is not Semantic Kernel. It is not a library you
add to your application. It is a tool you install once, configure per-project, and
invoke from your terminal — the way you invoke `git` or `dotnet`.

```bash
dotnet tool install -g phelix
phelix "add OpenTelemetry tracing to the OrdersService"
```

---

## 2. Design Philosophy

These constraints are non-negotiable. Every feature request gets measured against them.

### 2.1 Primitives, not features

Phelix ships with the minimum viable control loop. Features that other tools bake in
(sub-agents, plan mode, code review agents) are built by the user as **skills** or
left out entirely. The core stays small and auditable.

### 2.2 .NET-native, not .NET-compatible

The competitive advantage of Phelix is that it lives inside the .NET toolchain.
Roslyn (the C# compiler-as-a-service) is a first-class citizen — not an add-on.
The `dotnet` CLI is a built-in tool. NuGet metadata is readable context.
This is not possible to replicate in a TypeScript harness.

### 2.3 Minimal system prompt

Context is a scarce resource. Phelix's system prompt is short by design.
Project-specific guidance lives in `PHELIX.md` files (per-directory, per-project).
Skills load only when invoked. Nothing is injected speculatively.

### 2.4 Adapt Phelix to your workflow, not the other way around

Configuration lives in the repository, not in a cloud account. Skills are Markdown
files the developer writes. Tools are C# classes the developer can extend.
The harness bends to the codebase, not the other way around.

### 2.5 The harness is a control system

Every architectural decision traces back to closing a feedback loop:
sense current state → compare to goal → act → sense again.
If a proposed feature does not contribute to that loop, it does not ship in core.

---

## 3. Constraints

| Constraint | Value |
|---|---|
| Language | C# 14 |
| Runtime | .NET 10 |
| Distribution | `dotnet tool install -g phelix` |
| Target OS (MVP) | Linux (Fedora; WezTerm) |
| Target OS (future) | macOS, Windows |
| TUI library | Spectre.Console |
| Model abstraction | Microsoft.Extensions.AI (`IChatClient`) |
| Roslyn | Microsoft.CodeAnalysis (workspace APIs) |
| No sub-agents in core | Extensions only |
| No plan mode in core | Extensions only |
| No cloud account required | All config is local / repo-local |

---

## 4. Core Concepts

### 4.1 PHELIX.md

The project instruction file. Phelix loads `PHELIX.md` files at startup by walking
from the current working directory up to the git root, then from `~/.phelix/`.
Later files override earlier ones for conflicting keys.

The file is **not** a 1,000-line instruction manual. It is a table of contents
that points to deeper documentation and activates named skills.

```markdown
# PHELIX.md — OrdersService

## Project
<!-- AGENT: We will be review persistence layer choice. PostgreSQL is not final -->
ASP.NET Core 9 Web API. Clean Architecture. PostgreSQL via EF Core 10. 

## Skills
- dotnet-webapi        # REST conventions, controller patterns
- efcore               # migration commands, DbContext patterns
- opentelemetry        # tracing and metrics conventions

## Constraints
- Never modify files under /legacy — read-only reference only
- Always run `dotnet build` after any change to *.cs files when finished coding. Do not run after every minor change.
```

### 4.2 Skills

A skill is a Markdown file that Phelix loads into context on demand.
Skills are **feedforward guides** — they increase the probability that the model
gets something right before it tries.

```
~/.phelix/skills/dotnet-webapi.md
~/.phelix/skills/efcore.md
./phelix/skills/orders-domain.md   ← repo-local skill
```

Skills are loaded progressively — only when the model or the user invokes them.
They do not pollute the initial context window.

### 4.3 Tools

A tool is a C# class that Phelix exposes to the model as a callable function.
Tools are the **efferent** path — how the model acts on the codebase.

Built-in tools (MVP):

| Tool | Description |
|---|---|
| `ReadFile` | Read a file from disk |
| `WriteFile` | Write or create a file |
| `RunCommand` | Execute a shell command (sandboxed) |
| `DotnetBuild` | Run `dotnet build` and return structured output |
| `DotnetTest` | Run `dotnet test` and return structured output |
| `RoslynDiagnostics` | Return compiler errors/warnings for a file or project |
| `ListFiles` | List files matching a glob pattern |
| `SearchCode` | Text search across the codebase |

### 4.4 Sensors (Feedback Loop)

A sensor is a **computational feedback mechanism** — a deterministic check that
runs after the model acts and feeds the result back into context.

MVP sensors (run automatically after each file write):

| Sensor | Signal |
|---|---|
| Roslyn diagnostics | Compiler errors and warnings |
| `dotnet build` | Build success/failure + error output |
| `dotnet test --filter` | Test pass/fail for files touched in this turn |

Sensors close the control loop. They are what make Phelix a harness rather than
a wrapper.

### 4.5 Session

A session is the unit of work. A session has:

- A task (the initial prompt)
- A turn history (messages + tool calls + sensor results)
- A configuration snapshot (which skills were active, which model was used)

Sessions are persisted as newline-delimited JSON to `~/.phelix/sessions/`.
Each turn appends a JSON record. The file is append-only and human-readable.

---

## 5. Architecture

### 5.1 Layer map

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
│  Model      │              │  Tool Registry     │
│  Adapter    │              │  Phelix.Core.Tools │
│  (IChatClient)             │  built-in + custom │
└──────┬──────┘              └────────────────────┘
       │
┌──────▼──────────────────────────────────────────┐
│            Context Manager                      │
│         Phelix.Core.Context                     │
│  PHELIX.md · skills · compaction · history      │
└─────────────────────────────────────────────────┘
       │
┌──────▼──────────────────────────────────────────┐
│            Sensor Pipeline                      │
│         Phelix.Core.Sensors                     │
│  Roslyn · build · test · custom                 │
└─────────────────────────────────────────────────┘
```

### 5.2 Project structure

```
phelix/
├── src/
│   ├── Phelix.Cli/                  # Entry point — dotnet global tool
│   │   ├── Program.cs               # System.CommandLine root command
│   │   ├── Commands/
│   │   │   ├── RunCommand.cs        # phelix "do the thing"
│   │   │   ├── InitCommand.cs       # phelix init
│   │   │   └── SkillCommand.cs      # phelix skill list|add|remove
│   │   └── Phelix.Cli.csproj
│   │
│   ├── Phelix.Core/                 # All logic — no UI dependencies
│   │   ├── Agent/
│   │   │   ├── AgentLoop.cs         # The orchestration loop
│   │   │   ├── Turn.cs              # One turn: prompt → tools → sensors → response
│   │   │   └── AgentOptions.cs
│   │   ├── Context/
│   │   │   ├── ContextManager.cs    # Builds the context window each turn
│   │   │   ├── PhelixMdLoader.cs    # Finds and merges PHELIX.md files
│   │   │   ├── SkillLoader.cs       # Loads skills on demand
│   │   │   └── Compactor.cs        # Summarises old turns near context limit
│   │   ├── Tools/
│   │   │   ├── ToolRegistry.cs      # Discovers and registers tools
│   │   │   ├── ToolResult.cs        # Uniform result type
│   │   │   ├── Builtin/
│   │   │   │   ├── ReadFileTool.cs
│   │   │   │   ├── WriteFileTool.cs
│   │   │   │   ├── RunCommandTool.cs
│   │   │   │   ├── DotnetBuildTool.cs
│   │   │   │   ├── DotnetTestTool.cs
│   │   │   │   ├── RoslynDiagnosticsTool.cs
│   │   │   │   ├── ListFilesTool.cs
│   │   │   │   └── SearchCodeTool.cs
│   │   │   └── ITool.cs             # Tool contract
│   │   ├── Sensors/
│   │   │   ├── SensorPipeline.cs    # Runs sensors, collects signals
│   │   │   ├── ISensor.cs
│   │   │   ├── RoslynSensor.cs      # Compiler diagnostics
│   │   │   ├── BuildSensor.cs       # dotnet build result
│   │   │   └── TestSensor.cs        # dotnet test result
│   │   ├── Session/
│   │   │   ├── Session.cs
│   │   │   ├── SessionStore.cs      # Append-only JSON log
│   │   │   └── TurnRecord.cs
│   │   ├── Models/
│   │   │   └── ModelAdapterFactory.cs  # Builds IChatClient from config
│   │   └── Phelix.Core.csproj
│   │
│   └── Phelix.Tui/                  # Spectre.Console rendering
│       ├── AgentRenderer.cs         # Streams model output to terminal
│       ├── StatusBar.cs             # Model name · tokens · current tool
│       ├── Spinner.cs               # Tool-call progress indicator
│       └── Phelix.Tui.csproj
│
├── tests/
│   ├── Phelix.Core.Tests/
│   │   ├── Agent/
│   │   ├── Context/
│   │   ├── Sensors/
│   │   └── Tools/
│   └── Phelix.Integration.Tests/
│
├── docs/
│   ├── ARCHITECTURE.md              # this file
│   ├── skills/                      # bundled skill library
│   │   ├── dotnet-webapi.md
│   │   ├── efcore.md
│   │   └── opentelemetry.md
│   └── AGENTS.md                   # instructions for AI working on Phelix itself
│
├── PHELIX.md                        # Phelix's own harness config (meta)
├── phelix.sln
└── README.md
```

### 5.3 The orchestration loop

This is the heart of Phelix. Everything else exists to serve this loop.

```
┌─────────────────────────────────────┐
│         User submits task           │
└──────────────┬──────────────────────┘
               │
               ▼
┌─────────────────────────────────────┐
│     ContextManager builds prompt    │
│  system prompt + PHELIX.md +        │
│  active skills + turn history       │
└──────────────┬──────────────────────┘
               │
               ▼
┌─────────────────────────────────────┐
│     IChatClient → model call        │
│     (streaming, renders to TUI)     │
└──────────────┬──────────────────────┘
               │
    ┌──────────▼──────────┐
    │  Tool calls in      │
    │  model response?    │
    └──────────┬──────────┘
         Yes   │    No
               │         └──────────────────────────────────┐
               ▼                                            │
┌─────────────────────────────────────┐                    │
│     ToolRegistry dispatches         │                    │
│     tool call → result              │                    │
└──────────────┬──────────────────────┘                    │
               │                                           │
               ▼                                           │
┌─────────────────────────────────────┐                    │
│     SensorPipeline fires            │                    │
│     (Roslyn, build, test)           │                    │
│     signals appended to context     │                    │
└──────────────┬──────────────────────┘                    │
               │                                           │
               └───────────────────────────────────────────┘
                        loop back to model call
                        until no tool calls remain
                               │
                               ▼
               ┌───────────────────────────┐
               │  Session record appended  │
               │  Final response displayed │
               └───────────────────────────┘
```

---

## 6. Configuration

Phelix reads configuration from three sources, in priority order
(later overrides earlier):

1. `~/.phelix/config.json` — user-level defaults (model, API key source)
2. `PHELIX.md` files in the project tree — project-level instructions
3. CLI flags — per-invocation overrides (`--model`, `--no-sensors`)

### 6.1 config.json

```json
{
  "defaultModel": "claude-sonnet-4-6",
  "providers": {
    "anthropic": { "apiKeyEnvVar": "ANTHROPIC_API_KEY" },
    "openai":    { "apiKeyEnvVar": "OPENAI_API_KEY" }
  },
  "session": {
    "storePath": "~/.phelix/sessions"
  },
  "sensors": {
    "enabled": true,
    "runBuildOnWrite": true,
    "runTestsOnWrite": false
  }
}
```

---

## 7. The .NET Moat — Roslyn

This section describes the capability that no TypeScript harness can replicate.

Roslyn (`Microsoft.CodeAnalysis`) gives Phelix access to a full semantic model
of the C# codebase at any point during a session:

- **Diagnostics as sensor output** — compiler errors and warnings fed back to
  the model immediately after a file write, before the model declares success
- **Symbol resolution** — find all usages of a type, method, or property
  across the solution without grep (semantic search, not text search)
- **Syntax tree analysis** — detect architectural violations structurally,
  not by string pattern (e.g. "no direct instantiation of DbContext outside
  the infrastructure layer")
- **Code metrics** — cyclomatic complexity, coupling, method length —
  as objective sensor signals
- **Incremental compilation** — only re-analyse files that changed,
  so sensor feedback is fast

In the MVP, Phelix uses Roslyn for the `RoslynDiagnosticsTool` and
`RoslynSensor`. Future extensions will expose the full workspace API
to user-defined sensors.

---

## 8. Key Interfaces

These are the seams in the system. Understanding them is understanding Phelix.

### IChatClient (Microsoft.Extensions.AI)

The model abstraction. Phelix never calls a model SDK directly.
Every model provider is accessed through `IChatClient`.

```csharp
// From Microsoft.Extensions.AI
public interface IChatClient
{
    Task<ChatCompletion> CompleteAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteStreamingAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

Swapping from Claude to GPT-4o is a one-line config change.
Adding Ollama support is writing one adapter class.

### ITool

The tool contract. Every tool — built-in or user-defined — implements this.

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }          // JSON Schema for the model
    Task<ToolResult> ExecuteAsync(
        JsonElement input,
        CancellationToken cancellationToken);
}
```

### ISensor

The sensor contract. Sensors run after tool calls and inject signals.

```csharp
public interface ISensor
{
    string Name { get; }
    bool ShouldRun(TurnContext context);      // e.g. "did a .cs file get written?"
    Task<SensorResult> RunAsync(
        TurnContext context,
        CancellationToken cancellationToken);
}
```

### IContextContributor

Anything that injects content into the context window implements this.

```csharp
public interface IContextContributor
{
    int Priority { get; }                    // lower = earlier in context
    Task<IEnumerable<ChatMessage>> ContributeAsync(
        SessionContext session,
        CancellationToken cancellationToken);
}
```

---

## 9. What Phelix Does Not Do (MVP)

This list is as important as what it does do.

| Not in core | Rationale |
|---|---|
| Sub-agents | Build with a skill or extension |
| Plan mode | Build with a skill |
| Autonomous PR creation | Explicit human action |
| Web search | Out of scope for coding harness |
| Code review agent | Future sensor or extension |
| Cloud sync / accounts | All state is local |
| GUI / web interface | Terminal only |
| Windows support (MVP) | Linux first, then cross-platform |

---

## 10. Build Phases

### Phase 1 — Skeleton (weeks 1–2)
Goal: `phelix "hello"` runs, calls the model, streams output to terminal.

- `Phelix.Cli` project with `System.CommandLine`
- `Phelix.Core` project with `AgentLoop` (single turn, no tools)
- `Phelix.Tui` project with streaming text via Spectre.Console
- `IChatClient` wired to Anthropic via `Microsoft.Extensions.AI`
- Session append log (JSON)

### Phase 2 — Tools (weeks 3–4)
Goal: Phelix can read, write, and run commands.

- `ToolRegistry` with reflection-based discovery
- `ReadFileTool`, `WriteFileTool`, `RunCommandTool`, `ListFilesTool`
- `DotnetBuildTool`, `DotnetTestTool`
- Multi-turn loop (tool calls feed back into context)

### Phase 3 — Sensors (weeks 5–6)
Goal: The loop closes. Errors feed back automatically.

- `SensorPipeline`
- `RoslynSensor` — diagnostics after every `.cs` write
- `BuildSensor` — build result injected as context
- Status bar updated with sensor state

### Phase 4 — Context engineering (weeks 7–8)
Goal: `PHELIX.md` and skills work end-to-end.

- `PhelixMdLoader` — walk dirs, merge configs
- `SkillLoader` — on-demand skill injection
- `Compactor` — basic compaction when context limit approaches
- `SearchCodeTool`, `RoslynDiagnosticsTool` (tool-callable version)

### Phase 5 — Polish + release (weeks 9–10)
Goal: `dotnet tool install -g phelix` works. README is complete.

- Cross-platform path handling
- `phelix init` command (scaffolds `PHELIX.md`)
- `phelix skill list` command
- NuGet packaging + GitHub Actions CI
- Bundled skill library (`dotnet-webapi`, `efcore`, `opentelemetry`)

---

## 11. Technology Decisions — Rationale

| Decision | Why |
|---|---|
| `System.CommandLine` over third-party | Ships with .NET, no extra dependency, official Microsoft support |
| `Microsoft.Extensions.AI` over Semantic Kernel | Lower abstraction level, less opinion, easier to reason about, SK builds on top of it anyway |
| `Spectre.Console` over raw ANSI | Battle-tested, good WezTerm/Linux support, rich without being heavy |
| Roslyn over tree-sitter | Native C# semantics, not just syntax. First-class in the .NET SDK. |
| JSON session log over SQLite | Human-readable, append-only, no schema migrations, trivially greppable |
| Append-only session file | Audit trail, replayable, survives crashes without corruption |

---

*This document should be updated after each phase is complete.*
*Implementation notes live in `docs/decisions/`.*
