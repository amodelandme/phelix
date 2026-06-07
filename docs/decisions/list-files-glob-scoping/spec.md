# ListFilesTool — Glob Scoping Fix

**Status:** Approved  
**Phase:** Phase Queue  
**Date:** 2026-06-06

---

## Problem

`ListFilesTool` with a bare `*` pattern (or any pattern rooted at the project
root) walks `.git/`, `bin/`, and `obj/` in full. A single `*` call can return
hundreds of irrelevant paths, burns tokens, and produces a log that is hard to
read or act on.

---

## Goal

Exclude `.git`, `bin`, and `obj` directory subtrees from all `list_files`
results by default. The exclusion list must be visible as a first-class contract,
overridable at construction, and independently testable.

---

## Non-Goals

- Changing the glob syntax or pattern resolution logic
- Excluding files (as opposed to directory subtrees)
- Making the exclusion list configurable at runtime via the tool parameters

---

## Design

Add an `IReadOnlySet<string> ExcludedDirectories` property to `ListFilesTool`.
The constructor accepts an optional `IReadOnlySet<string>? excludedDirectories`
parameter; when `null`, it defaults to `{ ".git", "bin", "obj" }`.

After `ResolveGlob` returns matches, filter out any path where at least one
path segment (split on `Path.DirectorySeparatorChar`) exactly matches a name in
`ExcludedDirectories`. Segment matching (rather than substring or prefix
matching) prevents false positives on file names like `binary-search.cs` or
paths like `src/objstore/`.

The `Description` string is updated to guide the model toward scoped patterns
(e.g. `src/**/*.cs`) and to mention that `.git`, `bin`, and `obj` are excluded.

---

## Contract

- `ExcludedDirectories` defaults to `{ ".git", "bin", "obj" }` when no override
  is passed
- A path is excluded when any of its segments exactly matches a name in
  `ExcludedDirectories` (case-sensitive on all platforms)
- Filtering is applied after glob resolution, before sorting and truncation
- Passing an empty set disables all exclusions
- Tests can inject a custom set to verify exclusion behavior in isolation
