# ListFilesTool Recursion Depth — Implementation Notes

**Date:** 2026-06-24
**Branch:** fix/list-files-recursion-depth

---

## What shipped vs. the spec

Implemented as specced. Depth is selected in `ResolveGlob` from
`normalizedGlob.Contains("**")`: present → `SearchOption.AllDirectories`,
absent → `SearchOption.TopDirectoryOnly`. No new tool parameter was added.

---

## Why `Contains("**")` on the normalized glob

The check runs against `normalizedGlob` (backslashes already converted to `/`),
before the `dirPrefix` has its `**` stripped. This keeps the depth decision tied
to the user's original intent rather than the resolved search root. A pattern
like `src/**/*.cs` resolves `searchRoot = <root>/src`, `filePattern = *.cs`, and
recurses because the original glob contained `**`; `src/*.cs` resolves the same
root but lists only that directory.

Substring matching is sufficient because `*` is not a valid path character to
match literally here — any `**` in the pattern is the recursion token.

---

## Tests that changed

Three existing exclusion tests (`DefaultExclusions_ExcludesGitBinObj`,
`CustomExcludedDirectories_HonorsOverride`, `EmptyExcludedDirectories_IncludesAll`)
used `pattern="*"` while asserting a nested `src/Main.cs` appeared. They relied
on the old always-recursive behavior. Switched them to `**`, which is the correct
pattern for the recursive exclusion behavior they verify.

Added three tests for the new contract: `*` lists top-level only, `**/*` recurses,
and `src/*.cs` lists only that directory without descending into `src/sub`.

---

## Verified against the real repo

A bare `*` on the Phelix root now returns the 7 top-level files (matching
`find . -maxdepth 1 -type f`), while `**` and `**/*` return 141. Before the fix
all three returned the same recursive 140, which is what caused the model to
receive a truncated dump and fall back to `bash find`.
