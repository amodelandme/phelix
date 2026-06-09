# Implementation Notes: Session Log Naming

## What was built

`SessionContext` — a new `sealed record` in `Phelix.Core.Session` that carries the three
pieces of session identity: `SessionId` (UUID), `SessionName?` (sanitized user input),
and `StartedAt` (UTC timestamp). It replaces the old `SessionLogger.SessionId` static field.

`SessionContext.FileSlug` is a computed property that produces the canonical filename
component used by both `SessionLogger` and `SqliteSessionStore`:
`yyyy-MM-dd-<name>-<sessionId>` when named, `yyyy-MM-dd-<sessionId>` when not.

`SessionContext.Sanitize` is a pure static method that converts raw user input to a
filesystem-safe slug: trims whitespace, collapses runs of whitespace to single hyphens,
strips non-alphanumeric/hyphen/underscore characters, truncates to 60 chars, trims
trailing hyphens. Returns `null` on empty/invalid input.

`SessionContext.Create` is the normal construction path — generates a fresh UUID and
captures `DateTimeOffset.UtcNow` once. Tests that need deterministic timestamps construct
`SessionContext` directly via the record constructor.

## Changes from spec

None. Implementation follows the spec exactly.

## Propagation path

```
Program.cs  →  PhelixHost.Build(sessionName)
               SessionContext.Create(sessionName)
               ├── SqliteSessionStore(context)       → ~/.phelix/sessions/<fileSlug>.db
               └── PhelixSession(... context)
                   └── SessionLogger.AppendAsync(record, context)  → <fileSlug>.jsonl
```

`TurnRecord` gained `string? SessionName` as its third positional parameter, populated
from `context.SessionName` in `FromTurn`. Every turn row in SQLite carries the name
redundantly — no join needed to identify which session a row belongs to.

## Files changed

- `src/Phelix.Core/Session/SessionContext.cs` — new
- `src/Phelix.Core/Session/SessionLogger.cs` — removed `SessionId` static; `AppendAsync` now takes `SessionContext?` and `filePath` as separate optional params
- `src/Phelix.Core/Session/SqliteSessionStore.cs` — takes `SessionContext`; `session_name` column added; column ordinals updated in `GetTurnsAsync`
- `src/Phelix.Core/Session/TurnRecord.cs` — `SessionName?` added; `FromTurn` takes `SessionContext` instead of `string sessionId`
- `src/Phelix.Core/Session/PhelixSession.cs` — takes `SessionContext`; `SessionLogger.SessionId` references replaced with `context.SessionId`
- `src/Phelix.Cli/PhelixHost.cs` — takes `string? sessionName`; constructs `SessionContext.Create(sessionName)`
- `src/Phelix.Cli/Program.cs` — startup name prompt before `PhelixHost.Build` in interactive mode
- `src/Phelix.Cli/CliRenderer.cs` — `WritePromptLabel` added for the startup prompt
- `tests/Phelix.Core.Tests/Session/SessionContextTests.cs` — new: 19 tests covering `Sanitize`, `FileSlug`, and `Create`
- All existing test files that called `TurnRecord.FromTurn` or `new SqliteSessionStore(":memory:")` updated to new signatures
