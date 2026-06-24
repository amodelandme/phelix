# ListFilesTool — Recursion Depth from Glob

**Status:** Approved
**Phase:** Phase Queue
**Date:** 2026-06-24

---

## Problem

`ListFilesTool.ResolveGlob` always passes `SearchOption.AllDirectories` to
`Directory.GetFiles`, regardless of the pattern. As a result `*`, `**/*`, and
`**` all return the same recursive walk of the entire subtree. On the Phelix
repo a bare `*` returns 140 files instead of the 7 top-level files.

There is no way to express "list only this directory." A model that asks for a
non-recursive listing (the natural reading of `*`) gets a large recursive dump,
which is then truncated to 2000 characters before it reaches the model. In
practice the model receives a confusing partial result, produces an empty reply,
and on retry abandons `list_files` for `bash find` to get the answer it wanted.

This contradicts the tool's own `Description`, which tells the model to "use `**`
for recursive matching" — implying `*` is *not* recursive — and it contradicts
standard glob semantics where `*` matches within one directory and `**` crosses
directory boundaries.

This reverses the explicit non-goal "Changing the glob syntax or pattern
resolution logic" from the earlier `list-files-glob-scoping` decision. That
decision deliberately scoped itself to directory exclusions only; this one
addresses the recursion-depth defect it left untouched.

---

## Goal

Make recursion depth follow the pattern:

- A pattern **without** `**` lists only the directory it targets
  (`SearchOption.TopDirectoryOnly`).
- A pattern **with** `**` recurses (`SearchOption.AllDirectories`), as today.

So `*` returns top-level files only, while `**/*.cs` still finds nested files.
This aligns the tool's behavior with its description and with the glob
conventions the model already expects.

---

## Non-Goals

- Adding a separate `recursive` boolean parameter — depth is inferred from the
  pattern, keeping the tool's surface unchanged.
- Changing directory exclusion behavior (`.git`, `bin`, `obj`) — exclusions
  still apply after resolution, identically in both depth modes.
- Supporting `**` at arbitrary mid-pattern positions with per-segment matching
  (e.g. `src/**/test/*.cs` matching only under `test`). `**` continues to mean
  "recurse below the resolved search root"; only its presence or absence selects
  the `SearchOption`.
- Changing sorting, truncation, relative-path output, or the `max_results` cap.

---

## Design

In `ResolveGlob`, after computing `searchRoot` and `filePattern`, select the
search option from whether the original glob contains the `**` token:

```
SearchOption depth = normalizedGlob.Contains("**")
    ? SearchOption.AllDirectories
    : SearchOption.TopDirectoryOnly;

string[] all = Directory.GetFiles(searchRoot, filePattern, depth);
```

The existing `dirPrefix` extraction already strips `**` to locate the search
root, so a pattern like `src/**/*.cs` resolves `searchRoot = <root>/src`,
`filePattern = *.cs`, and now correctly recurses because the original glob
contained `**`. A pattern like `src/*.cs` resolves the same `searchRoot` but
lists only that directory.

The `Description` string is updated to state the contract explicitly: `*` lists
the matched directory only; `**` recurses.

---

## Contract

- A pattern containing `**` searches recursively (`AllDirectories`).
- A pattern without `**` searches a single directory (`TopDirectoryOnly`).
- `*` returns top-level files only; `**/*` returns all files recursively.
- Directory exclusions (`.git`, `bin`, `obj` by default) apply after resolution
  in both modes, unchanged.
- Sorting, relative-path output, `max_results` truncation, and error handling
  are unchanged.
