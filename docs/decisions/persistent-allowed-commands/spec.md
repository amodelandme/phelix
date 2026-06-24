# Spec: Persistent Allowed Commands

## Problem

The [bash-command-allowlist](../bash-command-allowlist/spec.md) decision made trusted
commands skippable, but only via the per-session `--accepts-commands` flag. That decision
deliberately rejected a config file to keep the grant visible at startup:

> Per-session via flag, not config file. Scope is visible at startup. No hidden state
> that a future session might silently inherit.

In practice, the flag-only model made the harness feel unusable for its primary user — the
developer running it on their own machine, all day, against their own repos. They retype
the same `--accepts-commands ls,cat,git,dotnet` on every launch, and the `Confirm` prompt
(type `yes` in full) fires constantly for commands they already trust unconditionally. The
friction the flag was meant to remove came back the moment the session restarted.

The trust decision belongs to the harness *user*. Shipped defaults stay safe; the user
declares, once, what they trust.

## Decision

Add a persistent `allowed_commands` list to `~/.phelix/config.yaml`. Its entries populate
the same allowlist seam the `--accepts-commands` flag already feeds. A `bash` call whose
first token matches an entry runs without the `Confirm`-tier prompt; everything else is
still gated.

The config list and the CLI flag are **additive**: the flag extends the trusted set for
the current run rather than overriding the config. This preserves the original "visible at
startup" property for ad-hoc grants while letting the standing, everyday set live in config.

```yaml
# ~/.phelix/config.yaml
allowed_commands: [ls, cat, git, dotnet, grep, find]
```

Out of the box `allowed_commands` is empty, so the default experience is unchanged: every
command is gated until the user opts in.

## Why this supersedes "no config file"

The original concern was *hidden state silently inherited by a future session*. Two things
make that acceptable now:

- **The state is not hidden.** It lives in the user's own, hand-edited config file — the
  same place they already set their model, provider, and retry policy. Editing it is a
  deliberate act, not an accident.
- **It is the user's machine and the user's grant.** Phelix is a single-developer CLI
  harness, built Linux-from-scratch style for its operator. The person who writes
  `allowed_commands` is the same person who will run the session. There is no third party
  to surprise.

The flag remains for the case the original decision optimized for: a one-off, this-session-
only grant whose scope you want visible on the command line.

## Matching rule

Unchanged from [bash-command-allowlist](../bash-command-allowlist/spec.md): exact match
against the first whitespace-delimited token of the `command` argument, case-sensitive, no
glob or regex. A user who allowlists `dotnet` knows exactly what they approved.

## Session modes interaction

Unchanged. The allowlist only fires where the gate would otherwise prompt for `Confirm`.
It has no effect in `AllowAll` (which uses `AutoApproveGate`) or on `Prompt`-tier calls.

| Mode | prefix NOT in allowlist | prefix IN allowlist (config OR flag) |
|---|---|---|
| `Default` | requires `yes` | silent approve |
| `AcceptsEdits` | requires `yes` | silent approve |
| `AllowAll` | silent approve (AutoApproveGate) | silent approve (AutoApproveGate) |

## Design

- `PhelixConfig` — add `IReadOnlySet<string> AllowedCommands` (defaults to empty).
- `FileConfigProvider` — deserialize `allowed_commands` (a YAML list) into the set; the
  `RawConfig` target gets a `List<string> AllowedCommands` defaulting to empty.
- `PhelixHost.Build` — union `config.AllowedCommands` with the CLI-derived
  `allowedCommandPrefixes`, then pass the union to `BuildApprovalGate`. The flag adds to,
  never replaces, the config set.
- `InteractiveApprovalGate`, `Program.cs` — unchanged; the existing seam and flag already
  do the right thing once they receive the merged set.

## What shipped

- `src/Phelix.Core/Config/PhelixConfig.cs` — `AllowedCommands` property; empty in `Default`.
- `src/Phelix.Core/Config/FileConfigProvider.cs` — parse `allowed_commands`.
- `src/Phelix.Cli/PhelixHost.cs` — merge config set with CLI flag before building the gate.

All 159 tests pass; no test asserted flag-only behaviour, so nothing regressed.

## Related: max-turns-per-turn default

Shipped alongside this change (same session): the fallback `MaxTurns` was corrected from a
stale `5` to `100` (`ModelConfig.DefaultMaxTurns`), matching the real default. `MaxTurns`
is a **per-turn** runaway guard that resets each prompt — not a session budget — so `100`
rounds per turn is an ample ceiling that still catches a stuck loop. This was a bugfix, not
a new feature, but it shares the same motivation: the out-of-box experience should be
usable, and the limit should be the user's to tune via config.

## For the project webpage

This is a "features" entry for the eventual harness webpage. One-line framing:

> **Trusted commands, declared once.** Pre-approve the shell commands you trust in your
> config — `ls`, `git`, `dotnet`, whatever — and Phelix stops asking. Everything else stays
> gated. Add a one-off grant for a single session with `--accepts-commands`.
