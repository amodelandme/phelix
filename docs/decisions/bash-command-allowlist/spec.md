# Spec: Bash Command Allowlist

## Problem

`bash` is `ApprovalTier.Confirm` — every shell command requires the user to type `yes`
before it runs. For commands the developer already trusts unconditionally for a session
(e.g. `dotnet`, `git`, `ls`), this friction is pure noise. The developer knows they want
those commands to run; they should not have to confirm them one by one.

At the same time, the allowlist must be explicit and per-session. It is not a config
file the user forgets about — it is a flag they type at startup, which makes the scope
of the grant visible and deliberate.

## Decision

`InteractiveApprovalGate` receives an optional `IReadOnlySet<string>` of allowed command
prefixes at construction time. When the gate is asked to approve a `bash` call and the
command's first token (the executable name) is in the allowlist, the gate approves
silently. All other `Confirm`-tier bash calls still require an explicit `yes`.

A new CLI flag `--accepts-commands <prefix,...>` populates the allowlist for the session.
The value is a comma-separated list of executable names (e.g. `dotnet,git,ls`).

## Matching rule

The allowlist is matched against the **first whitespace-delimited token** of the
`command` argument. This is the executable name — `dotnet` for `dotnet build ./Phelix.sln`,
`git` for `git status`. No glob or regex — exact string match only, case-sensitive.

Rationale: prefix matching is predictable and auditable. A user who allowlists `dotnet`
knows exactly what they are approving. Glob matching would make the grant harder to reason
about and easier to abuse via crafted commands.

## Session modes interaction

The allowlist only fires when the gate would otherwise prompt for `Confirm`. It has no
effect in `AllowAll` mode (which uses `AutoApproveGate`) and no effect on `Prompt`-tier
calls. The matrix:

| Mode | bash command, prefix NOT in allowlist | bash command, prefix IN allowlist |
|---|---|---|
| `Default` | requires `yes` | silent approve |
| `AcceptsEdits` | requires `yes` | silent approve |
| `AllowAll` | silent approve (AutoApproveGate) | silent approve (AutoApproveGate) |

## Design

- `InteractiveApprovalGate` — add `IReadOnlySet<string> allowedCommandPrefixes` constructor
  parameter (defaults to empty). In `RequestApprovalAsync`, when `tier == Confirm` and
  `toolName == "bash"`, extract the first token of the `command` arg and check it against
  the set before falling through to `PromptAsync`.
- `PhelixHost.Build` — accept `IReadOnlySet<string> allowedCommandPrefixes` and thread it
  into `BuildApprovalGate`, which passes it to `InteractiveApprovalGate`.
- `Program.cs` — add `Option<string?> --accepts-commands`. Parse by splitting on `,` and
  trimming each token into a `HashSet<string>`. Pass the set to `PhelixHost.Build`.

## Files to change

- `src/Phelix.Core/Agent/InteractiveApprovalGate.cs` — allowlist parameter and check
- `src/Phelix.Cli/PhelixHost.cs` — thread allowlist through `Build` and `BuildApprovalGate`
- `src/Phelix.Cli/Program.cs` — parse `--accepts-commands`
- `tests/Phelix.Core.Tests/Agent/ApprovalGateTests.cs` — new cases for allowlist behaviour
