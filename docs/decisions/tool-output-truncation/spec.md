# ListFilesTool — Relative Path Output

**Status:** Approved  
**Phase:** Phase Queue  
**Date:** 2026-06-06

---

## Problem

`ListFilesTool` returns absolute paths. Every result line carries the full root
prefix (e.g. `/home/sl00th/Dev/phelix/`) regardless of whether the caller has
any use for it. On a 65-file result, this adds roughly 1,900 characters — all
of which are stored verbatim in the session log and re-sent to the model on
every subsequent turn.

---

## Goal

Return paths relative to `RootDirectory` so results are compact, portable, and
readable without changing the tool's contract or behavior in any other way.

---

## Non-Goals

- Generic truncation across all tools (tracked separately in the roadmap)
- Changing glob resolution, sorting, exclusion logic, or `max_results` behavior
- Making path format configurable at construction or runtime

---

## Design

In `ExecuteAsync`, after `Array.Sort` and before the `StringBuilder` loop,
relativize each match with `Path.GetRelativePath(RootDirectory, matches[i])`.
No other code changes.

`Path.GetRelativePath` is deterministic and allocation-cheap. It handles the
case where `RootDirectory` and the match share no prefix by emitting `../`
segments — which cannot happen here because `ResolveGlob` always searches under
`RootDirectory`.

---

## Contract

- Results are paths relative to `RootDirectory`, using the platform path separator
- A file at `<root>/src/Foo.cs` is returned as `src/Foo.cs`
- A file at `<root>/Foo.cs` is returned as `Foo.cs`
- Sorting, exclusion, truncation, and the `(no files matched)` sentinel are unchanged
- The truncation notice (`... (truncated, N total matches — narrow the pattern)`) is unchanged

---

## Tests

One new test: `ListFilesTool_ResultsAreRelativePaths` — creates a file at
`src/Foo.cs` under the temp root, asserts the result contains `src/Foo.cs` and
does not contain the temp root prefix.

Existing tests are unaffected: they assert on filenames and substrings that
appear in both absolute and relative paths.
