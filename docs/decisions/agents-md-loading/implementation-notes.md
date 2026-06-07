# Implementation Notes: AGENTS.md Per-Repo Loading

## What was built

`AgentsMdLoader` — a static class in `Phelix.Core.Context` with two public methods:

- `Load(string baseSystemPrompt, string workingDirectory)` — the entry point. Checks
  for `AGENTS.md` in `workingDirectory`, reads it if present, returns the composed
  prompt. Falls back gracefully on absence or IO failure.
- `BuildComposedPrompt(string baseSystemPrompt, string agentsMdContent)` — pure
  composition logic, extracted so it can be tested independently of the filesystem.

`PhelixHost.Build()` calls `AgentsMdLoader.Load(config.SystemPrompt, Directory.GetCurrentDirectory())`
and passes the result into `AgentOptions.SystemPrompt`. Everything downstream is unchanged.

## Test coverage

Six tests in `Phelix.Core.Tests/Context/AgentsMdLoaderTests.cs`:

1. File absent → base prompt returned unchanged
2. File present → composed prompt contains both base and project content
3. File present → base prompt appears before project context
4. `BuildComposedPrompt` wraps base in `<system>` tags
5. `BuildComposedPrompt` wraps project content in `<project-context>` tags
6. Non-existent working directory → base prompt returned unchanged (same code path as absent file)

## Why `static` and not an interface

`AgentsMdLoader` has no injectable dependencies and no state. Extracting an
`IAgentsMdLoader` interface would be premature — there is currently no reason to
swap the implementation. If the Phase 4 `PhelixMdLoader` reuses this logic or
needs to be injected into a context pipeline, the refactor to an interface is
straightforward at that point.

## Synchronous file I/O

`File.ReadAllText` is synchronous. The alternative — making `PhelixHost.Build()`
async — would cascade into `Program.cs` (`Main` becomes `async Task`) for no
measurable benefit at startup with a single small file. The sync call is the
simpler and correct choice here.
