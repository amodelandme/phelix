# Spec: Session Log Naming

## Problem

Sessions are identified only by a date-time-prefixed UUID, making them hard to retrieve
after the fact. A session from three days ago named
`2026-06-06-4a9f1c2b...db` is opaque. A session named
`2026-06-06-auth-middleware-rewrite-4a9f1c2b...db` is immediately identifiable.

## Decision

At startup, in interactive mode, Phelix prompts the user for an optional session name.
Empty input skips naming and falls back to the date-time slug. The name is immutable for
the lifetime of the session — it is baked into the filenames on first write and into every
turn record persisted to SQLite.

Single-turn invocations (`phelix "prompt"`) skip the prompt entirely; the name is always
the date-time slug in non-interactive mode.

## SessionContext

A new `SessionContext` record replaces the raw `SessionLogger.SessionId` static. It
carries the three pieces of identity that define a session:

```csharp
public sealed record SessionContext(
    string SessionId,
    string? SessionName,
    DateTimeOffset StartedAt);
```

`SessionId` is a process-lifetime UUID (same generation as today's `SessionLogger.SessionId`).
`SessionName` is the sanitized user-supplied name, or `null` if skipped.
`StartedAt` is captured once at construction so filenames and records share the same timestamp.

`SessionContext` is constructed in `PhelixHost.Build` and passed to `PhelixSession`,
`SessionLogger`, and `SqliteSessionStore`. `SessionLogger.SessionId` (the static field) is
removed.

## Name sanitization

Before use in filenames or storage, the raw input is sanitized:

- Trim leading/trailing whitespace.
- Replace runs of whitespace with a single hyphen.
- Strip any character that is not a letter, digit, hyphen, or underscore.
- Truncate to 60 characters.
- If the result is empty after sanitization, treat as if the user skipped (name is `null`).

Sanitization is a pure static method on `SessionContext` — testable in isolation.

## File naming

Both log files use the same slug:

```
~/.phelix/sessions/<date>-<name>-<sessionId>.jsonl   (name present)
~/.phelix/sessions/<date>-<sessionId>.jsonl           (name absent)

~/.phelix/sessions/<date>-<name>-<sessionId>.db
~/.phelix/sessions/<date>-<sessionId>.db
```

`SessionContext` exposes a `FileSlug` computed property that produces the correct string
for the current instance. Both `SessionLogger` and `SqliteSessionStore` call `context.FileSlug`
— no naming logic is duplicated between them.

## SQLite schema

A `session_name` column (nullable TEXT) is added to the `turns` table:

```sql
CREATE TABLE IF NOT EXISTS turns (
    turn_id                  TEXT NOT NULL PRIMARY KEY,
    session_id               TEXT NOT NULL,
    session_name             TEXT,
    ...
)
```

`TurnRecord` gains a `string? SessionName` property. `TurnRecord.FromTurn` receives the
`SessionContext` (already passed as `sessionId` today) and populates both fields from it.

The name is redundant across rows for the same session — intentionally so. Each row is a
self-contained record; a future reader does not need to join to a sessions table to know
what session a turn belonged to.

## Startup prompt

In `Program.cs`, before `PhelixHost.Build`, in interactive mode only:

```
Session name (optional, press Enter to skip):
> 
```

The prompt is printed with `AnsiConsole` (dimmed label, standard input). Raw `Console.ReadLine`
is used for the actual read — consistent with the existing REPL input pattern.

The raw input is passed to `SessionContext.Sanitize`, which returns `null` on empty/invalid
input. The sanitized value (or `null`) is passed to `PhelixHost.Build`.

## Immutability

The name is set once at construction. There is no rename operation. A future rename feature
would require a separate `sessions` metadata table with a foreign key from `turns` — out of
scope here.

## Files to change

- `src/Phelix.Core/Session/SessionContext.cs` — new file: `SessionContext` record with `FileSlug` and `Sanitize`
- `src/Phelix.Core/Session/SessionLogger.cs` — remove `SessionId` static; accept `SessionContext`; use `context.FileSlug` in `DefaultFilePath`
- `src/Phelix.Core/Session/SqliteSessionStore.cs` — accept `SessionContext`; use `context.FileSlug` in `BuildConnectionString`; add `session_name` column
- `src/Phelix.Core/Session/TurnRecord.cs` — add `string? SessionName` property
- `src/Phelix.Core/Session/PhelixSession.cs` — replace `SessionLogger.SessionId` references with `context.SessionId`
- `src/Phelix.Cli/PhelixHost.cs` — accept `string? sessionName`; construct `SessionContext`; pass to session, logger, store
- `src/Phelix.Cli/Program.cs` — add startup name prompt in interactive mode; pass raw input to `PhelixHost.Build`
- `tests/Phelix.Core.Tests/Session/SessionContextTests.cs` — new file: sanitization and `FileSlug` cases
- `tests/Phelix.Core.Tests/Session/SessionLoggerTests.cs` — update construction; add named/unnamed path cases
