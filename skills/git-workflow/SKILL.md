---
name: git-workflow
description: Full git workflow for Phelix — branches, commits, PRs, and cleanup. Runs autonomously from dirty working tree to clean dev.
---

# Git Workflow

Run this skill to completion without stopping for confirmation unless a pre-merge
check fails or an ambiguous situation requires a decision. The goal is a clean `dev`
branch with the work merged and all feature branches deleted.

---

## Autonomy

This skill is evolving from human-driven toward autonomous. The dial is moved
**per gate, by hand** — there is no global level. Each gate below declares its current
**behavior** and a **graduates when** note: the condition that, once met, lets you loosen
it one notch. Loosening is a deliberate edit to this file, never automatic.

The three behaviors, from most to least human involvement:

- **ask** — stop and wait for the user before proceeding.
- **warn** — surface the issue, then proceed once acknowledged (or auto-resolve and note it).
- **auto** — decide silently and continue; report what was decided.

The loosening order is `ask → warn → auto`. One rule overrides everything: **security
gates do not move.** They are the permanent floor that makes automating the other gates
safe — a gate marked "does not graduate" stays a hard stop no matter how autonomous the
rest of the workflow becomes.

---

## Step 1 — Detect starting state

Run `git status` and `git branch --show-current`.

| Situation | Action |
|---|---|
| On `dev`, changes unstaged/staged | Proceed to Step 1a |
| Already on a `feature/*` branch, changes present | Skip to Step 3 |
| Already on a `feature/*` branch, working tree clean | Skip to Step 4 |
| On `dev`, working tree clean | Nothing to do — report and stop |
| On `main` or any other branch | Stop and ask the user |

### Step 1a — Enumerate concerns before branching

Inspect the changed and untracked files and group them into **distinct concerns**
(a feature, an unrelated docs edit, a refactor, a rename, etc.). One concern is a set
of files that belong in a single PR.

- **Exactly one concern** — proceed to Step 2.
- **More than one concern** — stop and ask the user how to proceed. Do not guess a split
  and do not sweep everything onto one branch. List the concerns you found and let the
  user decide which to branch first.

This guard is the point of the skill: a clean `dev` comes from one concern per branch,
never from bundling unrelated changes because they happened to be in the tree together.

> **Behavior: ask** (on multi-concern trees). Single-concern trees already proceed
> silently.
> **Graduates to warn when:** the skill has correctly identified concern boundaries on
> ~10 multi-concern runs with no miss — at which point it may *propose* a split and
> proceed on acknowledgment instead of a full stop. Full `auto` (split and branch the
> first concern unattended) only after the proposed splits have been accepted unchanged
> for a sustained stretch.

---

## Step 2 — Cut the feature branch

Branch from `dev`. Choose the prefix based on the nature of the changes:

| Prefix | When |
|---|---|
| `feature/` | new functionality |
| `fix/` | bug fixes |
| `chore/` | maintenance, dependencies, config |
| `docs/` | documentation only |

Slug: kebab-case, short, descriptive (e.g. `feature/otel-tracing`, `fix/tool-registry-duplicate`).
One concern per branch — do not combine unrelated changes.

```
git checkout -b <prefix>/<slug>
```

---

## Step 3 — Commit

Group changes into logical commits. Each commit should be a self-contained unit of
change. One commit is fine if the change is coherent; split only when two distinct
concerns are present.

**Format:** Conventional Commits with scope.

```
<type>(<scope>): <subject>
```

**Types:** `feat`, `fix`, `chore`, `docs`, `refactor`, `test`

**Scope:** the component or layer being changed (e.g. `cli`, `agent-loop`, `session`, `telemetry`)

**Subject rules:**
- 72-character maximum
- Imperative mood — "add tracing" not "added tracing"
- No trailing period

**Trailer:** end the commit message with the harness-mandated `Co-Authored-By`
trailer when one is in effect for the session. The harness requirement governs — do
not strip it. (Earlier versions of this skill forbade co-author lines; that rule is
withdrawn because it conflicts with the harness mandate.)

Stage files explicitly by name — never `git add -A` or `git add .`.

---

## Step 4 — Pre-merge checks

Three tiers, each with different consequences. Read the tier label before acting on a
failure — a security failure is not the same kind of event as a missing spec.

### 4a — Hard gates (a failure stops the run)

1. `dotnet build phelix.slnx` passes with zero errors and zero warnings.
   Always pass this explicit solution path — never bare `dotnet build`, which hits
   .NET CLI cache bugs.
2. This branch's own changes are fully committed — `git status` shows no staged or
   unstaged changes to any file belonging to **this** concern. Files left in the tree
   because Step 1a deferred them to another concern are expected and do not fail this
   check; confirm each remaining entry is a deferred concern, not forgotten work.
3. PR title (drafted next) follows conventional commit format.

If any hard gate fails: report which and why, then stop.

> **Behavior: ask** (stop on failure). These are objective pass/fail checks, not
> judgment calls, so there is little to "automate" — the check always runs.
> **Graduates when:** it does not. A failing build or an uncommitted-but-not-deferred
> file is never something to proceed past; this tier stays a hard stop. (Distinct from a
> security gate only in that the *consequence* is correctness, not danger.)

### 4b — Security gates (a failure stops the run — never auto-override)

These stop something *dangerous* becoming permanent history. Unlike the soft gate
below, these are never acknowledged-away; if one trips, stop and surface it to the user.

4. **Base branch is `dev`** — not `main` or any other protected/long-lived branch.
   Refuse to open or merge a PR that targets `main`.
5. **No force-push.** Never `git push --force` / `--force-with-lease` in this workflow.
   A feature branch that won't fast-forward is an ambiguous situation — stop and ask.
6. **No secrets in the diff.** Scan `git diff origin/dev...HEAD` for likely secrets
   before pushing: `.env` files, private keys (`BEGIN ... PRIVATE KEY`), `api_key` /
   `secret` / `token` / `password` assignments with literal values, AWS-style keys.
   Any hit stops the run — report the file and line, do not push.

If any security gate fails: stop, report exactly what tripped it, and wait for the user.

> **Behavior: stop. Does not graduate.** This tier is the permanent floor. As the rest of
> the workflow moves toward `auto`, these gates stay hard stops precisely so that
> increasing autonomy never becomes increasing danger. The only way they should *strengthen*
> is by becoming real tooling (a secret scanner, a pre-push hook) rather than prose — but
> they never loosen.

### 4c — Soft process gate (a failure warns — proceed on acknowledgment)

This is *project process hygiene, not security*. It belongs here only because the
push/merge is the last chokepoint where it can be caught — not because a missing doc is
dangerous. Do not treat it as a hard or security gate.

7. If a feature branch — a decision doc exists for the feature under
   `docs/decisions/<feature>/`: either `spec.md` or `implementation-notes.md`
   (spec-only is acceptable for small, self-contained changes, e.g.
   `dynamic-tool-loading`).

If the soft gate fails: warn, state that no decision doc was found, and proceed only
after the user acknowledges — or offer to write the doc, commit it, and re-run from 4a.
Never silently skip it, and never hard-block on it.

> **Behavior: warn.** Already past `ask` — it does not stop the run.
> **Graduates to auto when:** the skill has reliably judged whether a change warrants a
> doc and drafted acceptable specs unprompted across several runs. `auto` here means:
> detect the missing doc, write it, commit it, and continue — reporting that it did so,
> not asking first.

---

## Step 5 — Push and open PR

```
git push -u origin <branch>
```

PR title: same format as commit subject — `type(scope): description`.

PR body template:

```markdown
## What
<!-- one or two sentences — what changed and why -->

## Checklist
- [x] builds clean
- [x] this branch's changes are committed (deferred concerns, if any, noted)
- [x] base branch is `dev` (not `main`), no force-push, no secrets in diff
- [ ] decision doc under `docs/decisions/<feature>/` (spec or notes) — check or note why absent

## Refs
<!-- related decisions docs or spec files -->
```

No draft PRs — open only when ready to merge.

```
gh pr create --base dev --title "..." --body "..."
```

---

## Step 6 — Squash merge

**Stop and get explicit approval before running this step.** Squash-merge with
`--delete-branch` is irreversible: it rewrites history onto `dev` and deletes the remote
branch. The autonomous run pauses here. Report the PR number and title, then proceed only
after the user confirms the merge.

```
gh pr merge <number> --squash --delete-branch
```

`--delete-branch` deletes the remote branch automatically.

> **Behavior: ask.** This is the last gate to move, by design. The merge is irreversible
> (history rewrite + branch deletion), so it stays a hard stop until nearly every other
> gate is `auto` and the security floor (4b) has proven solid in practice.
> **Graduates to warn when:** the full pre-merge run (4a/4b/4c) has been trustworthy
> unattended for a long stretch — at which point the skill may announce the merge and
> proceed after a brief acknowledgment rather than a full stop. There is deliberately no
> path to `auto` written here yet; reaching it is a decision to make explicitly later, not
> a notch to slide into.

---

## Step 7 — Delete local branch and confirm

`gh pr merge --delete-branch` (Step 6) checks out `dev` and usually deletes the local
branch too, so `git branch -D` may report "branch not found" — that is success, not an
error. Run the cleanup defensively:

```
git branch -D <branch> 2>/dev/null || true
git remote prune origin
```

If the local branch did still exist, `-D` (force) is required after a squash merge —
git will not recognize it as merged because the squash produces a new SHA on `dev`.

Then run `git log --oneline -3` and `git status` and report:
- The merge commit SHA and title now on `dev`
- Both branches are gone (local and remote)
- The working tree state: clean, or — if Step 1a deferred other concerns — exactly
  those deferred files and nothing else
