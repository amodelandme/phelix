# XML Documentation Convention — Implementation Notes

**Date:** 2026-06-21

## What was done

Two changes, in sequence:

1. Wrote the convention spec (`spec.md`) defining a deletion litmus test for
   `<summary>` tags, reserving `<remarks>` for why/history/invariants, and
   exempting enum members from the test. Updated `AGENTS.md`'s two XML-doc
   mentions to point at the new spec. Flagged the original
   `docs/decisions/xml-documentation/spec.md` as superseded with a dated note,
   keeping it for historical record of the original coverage pass.

2. Applied the litmus test retroactively across `src/Phelix.Core` and
   `src/Phelix.Cli`. Removed 21 `<summary>` tags that were pure restatement of
   the member's signature or name, across 14 files. `BashTool`'s one real
   fact ("combined stdout/stderr") was folded into its existing `<remarks>`
   block rather than dropped outright.

## Files updated

**Convention (commit 1):**
- `AGENTS.md`
- `docs/decisions/xml-documentation/spec.md` — superseded notice
- `docs/decisions/xml-documentation-convention/spec.md` — new

**Cleanup (commit 2):**
- `src/Phelix.Cli/CliRenderer.cs`
- `src/Phelix.Cli/PhelixHost.cs`
- `src/Phelix.Core/Agent/TurnExitReason.cs`
- `src/Phelix.Core/Session/ISessionSummarizer.cs`
- `src/Phelix.Core/Session/ToolCallRecord.cs`
- `src/Phelix.Core/Session/TurnRecord.cs`
- `src/Phelix.Core/Telemetry/PhelixTelemetry.cs`
- `src/Phelix.Core/Tools/BashTool.cs`
- `src/Phelix.Core/Tools/ITool.cs`
- `src/Phelix.Core/Tools/ListFilesTool.cs`
- `src/Phelix.Core/Tools/ReadFileTool.cs`
- `src/Phelix.Core/Tools/SearchCodeTool.cs`
- `src/Phelix.Core/Tools/SearchSessionTool.cs`
- `src/Phelix.Core/Tools/WriteFileTool.cs`

## Decisions

**Enum members were never in scope for removal.** Even members that look like
restatement (e.g. a `Failed` value with a one-line summary) were left
untouched — the spec calls these out as exempt because the bare name often
hides a real ambiguity (dispatch failure vs. tool-logic failure).

**`ToolCallRecord.Status` summary was kept, not removed**, despite surface
resemblance to the flagged `Name` property on the same record. Its text
("whether the tool was successfully dispatched") disambiguates dispatch
success from the tool's own result — a real fact not visible from the
property name alone, so it passes the litmus test and was left in place.

**Class-level `<remarks>` blocks were never edited**, except `BashTool.cs`
where the deleted summary's only informative clause was merged into the
existing remarks rather than lost. No other remarks content was touched.

## Verification

`dotnet build phelix.slnx` — 0 errors, 0 warnings, both before and after the
cleanup commit. `dotnet test phelix.slnx` — 146/146 passing, unchanged.

## Follow-up

Migration is now complete — no further retroactive cleanup is pending. Future
documentation should be written directly against `spec.md`'s litmus test
rather than the original (superseded) unconditional-summary rule.
