# ListFilesTool Glob Scoping — Implementation Notes

**Date:** 2026-06-07  
**Branch:** fix/list-files-glob-scoping

---

## What shipped vs. the spec

Implemented exactly as specced.

---

## Segment matching via span walk

`HasExcludedSegment` splits the path by `Path.DirectorySeparatorChar` and
`Path.AltDirectorySeparatorChar` using a span walk rather than `string.Split`.
This avoids allocating a string array per path and prevents false positives on
file or directory names that merely contain an excluded name as a substring
(e.g. `objstore/`, `binary-search.cs`).

---

## Empty-set fast path

`ResolveGlob` short-circuits the filter entirely when `ExcludedDirectories.Count
== 0`, returning the raw `Directory.GetFiles` result without any per-path
allocation. This makes the opt-out case (pass an empty set) free.
