# Spec: AGENTS.md Per-Repo Loading

## Problem

Phelix's system prompt is a single harness-level string from `config.yaml`. There is
no mechanism to inject project-specific agent guidance without editing global config.
Project conventions, constraints, and domain context belong in the repo, not in a
personal config file.

## Decision

On startup, Phelix looks for `AGENTS.md` in the current working directory. When found,
its contents are appended to the base system prompt using XML-tagged sections. The model
receives both the harness-level behavioral baseline and the project-level instructions
in a single coherent prompt.

File absence is silent — no error, no warning. File read failure writes a warning to
stderr and falls back to the base prompt.

## Scope

CWD only. No git-root walking, no directory traversal. That belongs to the Phase 4
`PhelixMdLoader` (which will handle `PHELIX.md` files). This feature is a stepping
stone, not the full context engineering pass.

## Prompt composition

```
<system>
{config.SystemPrompt}
</system>

<project-context>
{AGENTS.md content}
</project-context>
```

XML tags delimit the two sources clearly for the model. The harness baseline always
comes first. Project context is additive — it extends, never replaces, the base prompt.

## Design constraints

- `Phelix.Core` owns the logic (`AgentsMdLoader` in `Phelix.Core.Context`), keeping
  it unit-testable without the CLI.
- `Phelix.Cli.PhelixHost` calls `AgentsMdLoader.Load` and passes the composed string
  into `AgentOptions.SystemPrompt`. `AgentLoop` and `AgentOptions` are unchanged.
- `File.ReadAllText` (synchronous) is used deliberately — blocking on one small file
  at startup is not a performance concern, and making `PhelixHost.Build()` async
  cascades into `Program.cs` for no benefit.

## Files changed

- `src/Phelix.Core/Context/AgentsMdLoader.cs` — new
- `src/Phelix.Cli/PhelixHost.cs` — call `AgentsMdLoader.Load` before building `AgentOptions`
- `tests/Phelix.Core.Tests/Context/AgentsMdLoaderTests.cs` — new
