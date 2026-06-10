# Implementation Notes: Bash Command Allowlist

## What shipped

Three files changed, five tests added (127 total, all pass).

### `InteractiveApprovalGate`

Added an optional `IReadOnlySet<string>? allowedCommandPrefixes` constructor parameter
(defaults to `null`). In `RequestApprovalAsync`, a new early-return branch fires when:

- `tier == ApprovalTier.Confirm`
- `toolName == "bash"`
- the set is non-null and non-empty
- the `command` arg's first whitespace-delimited token is in the set

The token extraction uses `ReadOnlySpan<char>` to avoid allocating a substring for the
comparison — `IndexOfAny(' ', '\t')` finds the boundary, `span[..space]` is the token,
then `.ToString()` only when the `Contains` check needs a `string`. If the command is
blank or all whitespace the token is empty and the check fails, so a blank command falls
through to the normal `PromptAsync` path.

The branch sits above the `tier switch`, so it takes priority over prompting but only
fires when all four conditions hold. All other behaviour is unchanged.

### `PhelixHost`

`Build` and `BuildApprovalGate` both gained `IReadOnlySet<string>? allowedCommandPrefixes`
parameters (optional, default `null`). `BuildApprovalGate` forwards the set to
`InteractiveApprovalGate`; it is ignored when `AllowAll` selects `AutoApproveGate`.

### `Program.cs`

`Option<string?> --accepts-commands` added to the root command. The value is split on
`,` with `RemoveEmptyEntries | TrimEntries` so `dotnet, git` and `dotnet,git` behave
identically. The resulting `HashSet<string>` is passed to `PhelixHost.Build`. When the
flag is omitted, an empty set is passed; the gate's `Count > 0` guard makes empty
equivalent to no allowlist.

## Design decisions

- **Exact first-token match, not substring or glob.** Predictable, auditable, harder to
  abuse. A user who types `--accepts-commands dotnet` knows `dotnet` commands will run;
  they are not guessing what patterns might match.
- **Gate-side, not tool-side.** The allowlist is policy, not tool behaviour. `BashTool`
  stays unaware of it; `InteractiveApprovalGate` is already the policy enforcement point.
- **Per-session via flag, not config file.** Scope is visible at startup. No hidden state
  that a future session might silently inherit.
- **`AllowAll` unaffected.** `AutoApproveGate` approves everything already; threading
  the allowlist into it would be dead code.
