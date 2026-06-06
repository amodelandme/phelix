# Skill: Git Workflow

Rules for all git operations on this project. Follow these exactly — do not
improvise branching names, commit formats, or merge strategy.

---

## Branches

**Prefixes:**
- `feature/` — new functionality
- `fix/` — bug fixes
- `chore/` — maintenance, dependencies, config
- `docs/` — documentation only

**Rules:**
- Always cut from `main`
- Slug is kebab-case, short, descriptive (e.g. `feature/otel-tracing`, `fix/tool-registry-duplicate`)
- One concern per branch — do not combine unrelated changes
- Delete branch after merge (local and remote)

---

## Commit Messages

Format: [Conventional Commits](https://www.conventionalcommits.org/) with scope.

```
<type>(<scope>): <subject>
```

**Types:** `feat`, `fix`, `chore`, `docs`, `refactor`, `test`

**Scope:** the component or layer being changed (e.g. `agent-loop`, `tool-registry`, `cli`, `session`, `telemetry`)

**Subject rules:**
- 72-character maximum
- Imperative mood — "add tracing" not "added tracing"
- No trailing period
- No `Co-authored-by` lines

**Examples:**
```
feat(telemetry): add ActivitySource and span name constants
fix(agent-loop): set error status on unhandled exception
chore(cli): add OTel exporter packages
docs(decisions): write OTel implementation notes
refactor(tool-registry): extract ITool dispatch to helper
```

---

## Pull Requests

**Title:** same format as commit subject — `type(scope): description`

**Merge strategy:** squash merge only

**No draft PRs** — open a PR only when the work is ready to merge

**Template:**

```markdown
## What
<!-- one or two sentences — what changed and why -->

## Checklist
- [ ] builds clean
- [ ] no uncommitted changes on the branch
- [ ] implementation-notes.md written (if feature branch)
- [ ] branch targets main

## Refs
<!-- related issues, decisions docs, or spec files -->
```

---

## Pre-merge Checks

Before merging, verify and flag if any of these fail:

1. `dotnet build <solution>` passes with zero errors and zero warnings
2. No uncommitted or unstaged changes on the branch
3. Branch targets `main`, not another feature branch
4. If a feature branch — `docs/decisions/<feature>/implementation-notes.md` exists
5. PR title follows conventional commit format

Do not merge if any check fails. Report which check failed and why.

---

## Post-merge Cleanup

After a successful merge:

1. Delete the remote branch: `git push origin --delete <branch>`
2. Delete the local branch: `git branch -D <branch>`
3. Confirm deletion of both

`-D` (force) is always required after a squash merge. Squash merge produces a
new commit SHA on main — git does not recognize the local branch as merged and
will refuse `-d`.
