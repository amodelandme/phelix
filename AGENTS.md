# AGENTS.md — Phelix

## What this repo is

Phelix is a minimal CLI coding harness for .NET developers.
Architecture document: `docs/ARCHITECTURE.md`. Read it before anything else.

## Agent-first conventions

- **No `var`.** Always write explicit types. Agents cannot infer — they read what is written.
- **XML documentation on all public/internal members.** Include `<param>`, `<returns>`, and a `<remarks>` block explaining *why* a design decision was made, not just what the code does.
- **Business-rule test names.** Test method names encode the domain rule being verified, not the method being called. Example: `RunTurnAsync_WhenHistoryIsEmpty_SendsSingleUserMessage`.
- **Strong types over primitives.** Prefer named types (`ModelId`, `SessionId`) over raw `string` where the domain warrants it.
- **Explicit `Result<T, TError>` return types.** Named failure cases over exceptions for recoverable errors.

## Coding standards

- **Clean Architecture.** Dependencies point inward. `Phelix.Core` has no
  reference to `Phelix.Cli` or `Phelix.Tui`. The compiler enforces this.
- **Interfaces at every seam.** `ITool`, `ISensor`, `IContextContributor`,
  `IChatClient`. Program to the abstraction, inject the implementation.
- **One responsibility per class.** If you can't describe what a class does
  in one sentence without the word "and", it does too much.
- **Explicit over implicit.** No magic. No reflection-heavy frameworks.
  If it isn't obvious from reading the code, rewrite it.
- **Readability and Simplicity are features.** Future readers include a less experienced
  developer and a coding agent. Write for both.
- **Security by default.** `RunCommandTool` sandboxes. File writes are
  path-validated. No user input reaches a shell unsanitized. Ever.

## Format conventions

Choose formats based on who consumes the output:

| Layer | Format |
|---|---|
| Agent context files | Markdown (`AGENTS.md`, skills, docs) |
| Prompt structure | XML tags for labeled sections |
| Domain docs / ADRs | Markdown with clear headers |
| API contracts | JSON (OpenAPI/Swagger) |
| Agent tool results | JSON → parsed by orchestrator |
| Human-facing output | Markdown |
| Config files | YAML |
| Append-only logs / streams | JSONL — each line self-contained, chunks cleanly |

**Meta-rule:** Markdown for humans-and-agents. XML for structuring agent input. JSON for machine-to-machine. JSONL for any time-ordered, append-only data. Never use JSON as a knowledge store an agent retrieves from — it chunks terribly across object boundaries.

## What belongs in core

Only what is necessary to close the control loop:
model call → tool dispatch → sensor feedback → next turn.

If it can be a skill or an extension, it is not core.

## Testing expectations

Every class in `Phelix.Core` must be unit-testable without a real model,
real file system, or real terminal. Use fakes, not mocks where possible.
Sensors and tools get integration tests separately.

## Working style

This project is a learning environment as much as a build environment. The developer is motivated to become an expert — not just in shipping features, but in understanding what is happening under the hood in C#, .NET, and LLM harness design.

**The gate rule:** Do not write code without an explicit go-ahead from the developer. The sequence is always:
1. Explain the concept and the design decision
2. Developer asks questions — go as deep as needed
3. Developer says "go" (or equivalent)
4. Then, and only then, write the code

**On deep dives:** Treat every feature as a window into something deeper. When a new concept appears — async I/O, interface dispatch, state machines, the Roslyn pipeline — surface it. Don't just show the code that uses it; explain what the runtime is actually doing.

**Code must tell a story.** Both agents and humans should be able to read it without a guide:
- No abbreviated variable names. `remainingTurnsBeforeHardStop` over `maxTurns`.
- No magic numbers or strings. Named constants with explanatory names.
- No clever constructs. Boring and readable beats compact and opaque.
- Identifiers are sentence fragments. A reader should understand intent from the name alone.
- Include XML Documentation for use by agents reading/scanning the codebase. Make it easy for them to understand.

## Branch strategy

- Active development happens on `dev`. Feature branches cut from `dev` and merge back into `dev`.
- `main` is updated only at major milestones — never commit directly to `main`.
- Branch hierarchy: `feature/*` (or `fix/*`, `chore/*`, `docs/*`) → `dev` → `main`

## Workflow

1. Read the relevant section of `ARCHITECTURE.md`
2. Understand the interface before implementing the class
3. Write the test before the implementation when practical
4. Build — then explain what was just built and why it works

## What we do not do

- No generated boilerplate without explanation
- No NuGet packages added without justification
- No clever code — boring and correct beats clever and fragile
- No skipping steps to go faster

## Skills

Project skills live in `skills/`. Each skill is a directory containing a `SKILL.md` file.
Load a skill when its domain is relevant to the current task.

| Skill | Path | When to load |
|---|---|---|
| Git Workflow | `skills/gitWorkflow/SKILL.md` | Any git operation — branching, commits, PRs, merges |

## Progress

- Review the PROGRESS.md file to verify the current state of development.
