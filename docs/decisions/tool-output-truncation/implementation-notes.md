# ListFilesTool — Relative Path Output: Implementation Notes

**Date:** 2026-06-06

## What changed

`ListFilesTool.ExecuteAsync` — one line changed in the `StringBuilder` loop:

```csharp
// before
sb.AppendLine(matches[i]);

// after
sb.AppendLine(Path.GetRelativePath(RootDirectory, matches[i]));
```

`Path.GetRelativePath` computes the relative path from `RootDirectory` to each
match. On a 65-file result with a 30-character root prefix, this removes roughly
1,900 characters from every tool result — reducing both session log size and
tokens sent to the model on subsequent turns.

## Test added

`ListFilesTool_ResultsAreRelativePaths` — creates `src/Foo.cs` under the temp
root, asserts the result contains `src/Foo.cs` (platform separator) and does
not contain the temp root path.

## Files touched

- `src/Phelix.Core/Tools/ListFilesTool.cs`
- `tests/Phelix.Core.Tests/Tools/ListFilesToolTests.cs`
