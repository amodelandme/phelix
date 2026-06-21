# XML Documentation Convention

**Status:** Approved
**Phase:** Phase Queue
**Date:** 2026-06-20
**Supersedes:** the documentation standard in `docs/decisions/xml-documentation/spec.md`

---

## Problem

The original XML documentation pass (`docs/decisions/xml-documentation/`) mandated
a `<summary>` on every public/internal type, method, property, and field
unconditionally. That produced good coverage, but an audit of the result found
roughly 29% of `<summary>` tags are pure restatement of the member's signature —
text that adds no information beyond what the identifier and type already
convey. Example: `string Name => "bash"` documented as `/// <summary>The name
the model uses to call this tool.</summary>` — the property name plus its
interface (`ITool.Name`) already says this.

`<remarks>` blocks did not have this problem. A census of all 62 `<remarks>`
blocks in `src/` found ~95% explain something genuinely unrecoverable from
the code body alone — a design tradeoff, a cross-class invariant, a bug that
was fixed and why, a deliberate imprecision. The original spec's instinct that
remarks are "the most important block for agent readability" was correct. The
mandate to also write a summary on every member regardless of whether it adds
information was not.

This matters specifically because the audience for this documentation is an
agent reading the file into context on every turn it touches that file. Tokens
spent on a summary that restates the identifier are tokens the agent pays on
every read, for zero gained understanding. Tokens spent on a remarks block
explaining a non-obvious invariant prevent the agent from re-deriving (or
mis-deriving) that fact from scratch — often via an extra tool call to check
git history or another file.

---

## Decision

### The litmus test

Before writing or keeping a `<summary>`, ask: **if this line were deleted,
would a reader reach a wrong conclusion, or have to go elsewhere (the method
body, another file, git history) to recover this fact?**

- If no — the signature already says it — omit the `<summary>` entirely.
- If yes — keep it, and keep it to one sentence.

This is a deletion test, not a writing test. Apply it to existing docs during
review and to new docs before committing them.

### `<summary>` — only when it adds a contract fact

Write a `<summary>` when it states something the signature cannot:

- A default value, format, or unit (`"capped at AgentLoop.MaxToolOutputChars"`)
- A throws-vs-returns-error convention (`"never throws — errors are returned
  as descriptive strings"`)
- A cross-reference to a consuming type that isn't otherwise visible
  (`"consumed exclusively by SessionLogger"`)
- A behavioral nuance an identical-looking signature elsewhere does NOT have

Do not write a `<summary>` that just rephrases the member name. Skipping the
tag is correct and preferred over a low-information one-liner. This applies
most often to:

- Simple properties whose name is the contract (`RootDirectory`, `Name`,
  `Description` on an `ITool`)
- Classes whose name and one method already state their purpose
  (`ReadFileTool`, `WriteFileTool`)
- Enum members where the name is unambiguous on its own — but see the
  exception below

**Exception — enum members are exempt from the litmus test.** A bare enum
value (`Failed`, `Skipped`, `Succeeded`) often hides a real ambiguity the name
alone doesn't resolve — e.g. whether `ToolCallStatus.Failed` means the tool
threw or that no tool was found by that name. Always document enum members.

### `<remarks>` — reserved for what the code cannot say about itself

A `<remarks>` block exists only to carry information the code body does not
contain:

- **Why**, not what — a design tradeoff, the alternative that was rejected
  and why
- **History** — a bug this fixed, with enough detail to recognize the bug
  class again (see `SqliteSessionStore.cs`'s FTS5 sanitizer remarks as the
  reference example)
- **Invariants spanning multiple types** — a guarantee one class depends on
  another class upholding, where neither file alone makes that dependency
  visible
- **Deliberate imprecision or limitation** — a heuristic that is intentionally
  approximate, and why the approximation is safe (see
  `TokenThresholdPolicy.cs`)

If a draft `<remarks>` block, with the code visible beside it, says nothing
the reader couldn't infer in a few seconds from the method body — delete it.
Restating control flow in prose is not a remarks block; it is a worse version
of the code.

### Methods

- `<summary>` only if the litmus test passes — most methods with a clear,
  well-named signature and short body do not need one
- `<param>` only for parameters whose constraint or meaning isn't obvious from
  the name and type (a `string path` parameter named `path` rarely needs one;
  a `string command` that gets passed to `/bin/sh -c` arguably does, to note
  it is not shell-escaped by the caller)
- `<returns>` when the return value's meaning depends on context not visible
  in its type (e.g. a `bool` that means "approved," not just "success")
- `<exception>` always, when the method can throw a documented exception type
  under specific conditions — this is a contract fact a caller needs and
  cannot get from the signature

### Classes and interfaces

The highest-value `<remarks>` placement in the codebase is the class or
interface level — this is where a reader orients themselves before reading
any member. Prioritize a strong class-level `<remarks>` over thorough
member-level summaries. A class with one excellent `<remarks>` block and no
property summaries is more useful to an agent than the reverse.

---

## Non-goals

- This does not relax the requirement to document enum members, exceptions, or
  genuine invariants — coverage of *meaningful* documentation is not being
  reduced, only restatement is being removed
- This is not a mandate to strip all existing remarks blocks — the audit found
  the existing remarks are already high-quality; this convention exists so
  future docs match that bar, and so the cleanup pass (tracked separately)
  knows what to remove versus keep
- Not a rule about code comments generally — `AGENTS.md`'s broader
  documentation philosophy (readability, naming, no magic numbers) is
  unchanged

---

## Reference examples from this codebase

**Worth keeping (high information density relative to token cost):**

- `src/Phelix.Core/Agent/ControlCharSanitizer.cs` — class remarks state the
  actual security threat model (ANSI/control-char injection into an approval
  prompt), not just what the sanitizer does
- `src/Phelix.Core/Session/SqliteSessionStore.cs` — `SanitizeFtsQuery` remarks
  document a real bug (`"AGENTS.md"` breaking FTS5 syntax) and why the fix is
  sufficient
- `src/Phelix.Core/Agent/Turn.cs` — class remarks explain why `Turn` and
  `TurnRecord` deliberately diverge in shape, preventing a reader from
  "simplifying" them into one type
- `src/Phelix.Core/Session/TokenThresholdPolicy.cs` — remarks state the
  chars/4 estimate is deliberately imprecise and why the safety margin makes
  that acceptable

**Should not have existed (pure restatement, now candidates for deletion):**

- `src/Phelix.Core/Tools/ReadFileTool.cs` class summary — "Reads the contents
  of a file and returns them as a string." on `class ReadFileTool`
- `src/Phelix.Core/Tools/BashTool.cs` class summary — restates the class name;
  the one real fact ("combined stdout/stderr") belongs in the existing remarks
  block instead, not duplicated in a summary
- `src/Phelix.Core/Session/ToolCallRecord.cs` — `Name` property summary "The
  name of the tool the model requested to invoke." adds nothing over the
  property name and its containing record name

---

## Migration

A cleanup pass is tracked separately (not part of this spec) to apply the
litmus test retroactively across `src/` — removing restatement summaries,
leaving remarks blocks untouched unless they fail the same test. This spec
defines the standard new code is held to immediately; the cleanup brings
existing code in line with it.
