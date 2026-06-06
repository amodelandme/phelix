# Phase 2 — Tools

**Status:** Draft  
**Phase:** 2  
**Date:** 2026-06-02

---

## Problem

The agent can read one file. That is the only thing it can do to the world.

A coding agent without write, shell, and search capabilities cannot close the
feedback loop: it can observe state but not change it, and it cannot verify
whether a change it requested actually worked. Phase 2 gives the agent the
minimal tool set needed to do useful coding work end-to-end.

---

## Goal

Add five built-in tools so the agent can read, write, run commands, list files,
and search code. Every tool follows the same contract established by `ReadFileTool`
in Phase 1. No new interfaces, no new abstractions.

---

## Non-Goals

- `DotnetBuildTool`, `DotnetTestTool`, `RoslynDiagnosticsTool` — Phase 3 (sensor
  pipeline). These require `SensorPipeline` context to be useful.
- Sandboxing or permission prompts for `RunCommandTool` — deferred.
- Streaming tool output — deferred.
- Tool composition or chaining — not in core.

---

## Tools

### 1. `write_file`

Writes text content to a file at the given path.

**When the model calls it:** after deciding what content a file should contain.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `path` | string | yes | Path to write, relative or absolute |
| `content` | string | yes | Full file content to write |

**Behavior:**

- Resolves `path` to an absolute path and checks it against `RootDirectory`.
  Refuses writes outside the root.
- Creates intermediate directories if they do not exist.
- Overwrites the file if it already exists. No merge, no diff — the model owns
  the full content.
- Returns `"OK"` on success.
- Returns a descriptive error string on any failure. The agent loop feeds this
  back to the model so it can recover.

**Security note:** same root-confinement pattern as `ReadFileTool`. Path traversal
(`../../etc/passwd`) is blocked by resolving to absolute before the prefix check.

---

### 2. `run_command`

Runs a shell command and returns its combined stdout and stderr.

**When the model calls it:** to run builds, tests, linters, formatters, or any
other shell operation.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `command` | string | yes | The shell command to run |
| `working_directory` | string | no | Working directory for the process. Defaults to `RootDirectory`. |
| `timeout_seconds` | int | no | Maximum time to wait. Defaults to 30. Max 120. |

**Behavior:**

- Executes via `/bin/sh -c <command>`. This is intentional — the model produces
  complete shell expressions, not argument arrays.
- Captures stdout and stderr into a single string, preserving interleaving order.
- Returns a result block:
  ```
  Exit code: 0
  ---
  <stdout + stderr>
  ```
- If the process exceeds `timeout_seconds`, kills it and returns:
  ```
  Error: command timed out after <N> seconds.
  ```
- The working directory is validated against `RootDirectory`. Commands are not
  sandboxed — the agent runs as the current user with full file-system access.
  This is a known limitation for MVP; a permission prompt or allowlist is deferred.

---

### 3. `list_files`

Lists files matching a glob pattern.

**When the model calls it:** to discover what files exist before reading or writing
them, or to understand the structure of an unfamiliar directory.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `pattern` | string | yes | Glob pattern relative to `RootDirectory` (e.g. `src/**/*.cs`) |
| `max_results` | int | no | Cap on returned paths. Defaults to 200. |

**Behavior:**

- Resolves the pattern relative to `RootDirectory`.
- Uses `Directory.GetFiles` with `SearchOption.AllDirectories` for `**` patterns.
- Returns one absolute path per line, sorted lexicographically.
- If the result count exceeds `max_results`, appends a trailing note:
  ```
  ... (truncated, 847 total matches — narrow the pattern)
  ```
- Returns an error string if the pattern is syntactically invalid.

---

### 4. `search_code`

Searches file contents for a literal string or regular expression.

**When the model calls it:** to find where a symbol, string, or pattern is defined
or used, before reading specific files.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `pattern` | string | yes | Literal string or .NET regex to search for |
| `file_glob` | string | no | Glob to restrict which files are searched. Defaults to `**/*`. |
| `is_regex` | bool | no | Treat `pattern` as a .NET regex. Defaults to `false`. |
| `max_results` | int | no | Cap on returned matches. Defaults to 50. |

**Behavior:**

- Walks files in `RootDirectory` matching `file_glob`.
- For each file, reads line by line and tests each line against `pattern`.
- Returns matches in the format:
  ```
  path/to/file.cs:42: matching line content
  ```
- If `max_results` is reached, appends:
  ```
  ... (truncated — narrow the pattern or file_glob)
  ```
- Binary files are skipped silently.
- Regex compilation errors are returned as error strings so the model can fix the
  pattern and retry.

---

## Shared design rules

These rules apply to every tool in this phase and every tool that ships in future
phases. They codify the patterns established in `ReadFileTool`.

### Root confinement

Every tool that touches the file system accepts a `rootDirectory` constructor
parameter (defaulting to `Directory.GetCurrentDirectory()`). All paths are
resolved to absolute before any operation, then checked with
`absolutePath.StartsWith(RootDirectory, StringComparison.Ordinal)`. Anything
outside the root gets a descriptive error string, not an exception.

### Return strings, not exceptions

`ExecuteAsync` always returns a `Task<string>`. Exceptions from I/O, process
execution, or argument parsing are caught and turned into error strings beginning
with `"Error:"`. The agent loop feeds these back to the model — the model can
read the error, adapt, and retry. Only unrecoverable programmer errors (null
dereference, unhandled state machine cases) are allowed to propagate as exceptions.

### Parameters are a dictionary

`ExecuteAsync(IReadOnlyDictionary<string, object?> parameters, ...)` is the
canonical signature. All tools use it. `ToAITool()` wraps a strongly-typed
lambda via `AIFunctionFactory.Create` and re-routes into `ExecuteAsync` so the
agent loop dispatches uniformly through `ToolRegistry`.

### Descriptive errors

Error strings name the parameter, the value, and the reason. The model reads
them. "Error: required parameter 'path' is missing." beats "Bad request."
"Error: path '/tmp/evil' is outside the allowed root '/home/user/proj'." beats
"Access denied."

---

## File layout

All new files land under the existing `Tools/` directory. No new subdirectory
is needed for five files.

```
src/Phelix.Core/Tools/
    ITool.cs               ← unchanged
    ToolRegistry.cs        ← unchanged
    ReadFileTool.cs        ← unchanged
    WriteFileTool.cs       ← new
    RunCommandTool.cs      ← new
    ListFilesTool.cs       ← new
    SearchCodeTool.cs      ← new
```

`Program.cs` registers all four new tools the same way `ReadFileTool` is
registered today — one `toolRegistry.Register(new XxxTool())` line each.

---

## What changes in existing files

### `Program.cs`

Four additional `toolRegistry.Register` calls. Nothing structural changes.

### `AgentLoop.cs`

No changes. The multi-turn tool loop is already complete from Phase 1. Phase 2
is purely additive — new tool implementations, no loop changes.

---

## Testing

Each tool gets a test class in `Phelix.Core.Tests/Tools/`. Tests use a
temporary directory (`Path.GetTempPath()` + a random folder name) as the root,
created in setup and deleted in teardown.

Key cases per tool:

| Tool | Cases |
|---|---|
| `WriteFileTool` | happy path, creates directories, overwrites existing, path outside root |
| `RunCommandTool` | exit 0, exit non-zero, timeout, working directory respected |
| `ListFilesTool` | matching files, no matches, truncation at `max_results`, bad pattern |
| `SearchCodeTool` | literal match, regex match, regex error, truncation, `file_glob` filter |

`ReadFileTool` tests already exist and serve as the pattern.

---

## Open questions

None blocking implementation. The following are noted for future phases:

- `RunCommandTool` has no allowlist or permission prompt. A user running Phelix
  in an untrusted environment (e.g. against a repo with a malicious `PHELIX.md`)
  could be tricked into running arbitrary commands. This is a known gap; the fix
  belongs in a security/sandbox phase, not Phase 2.
- `max_results` defaults are guesses. They will be tuned once real sessions
  accumulate in `~/.phelix/sessions/`.
