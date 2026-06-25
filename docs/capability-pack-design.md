# Capability Packs & Shift-Left — Design Session Summary

> **Status:** Pre-spec. Design resolved; ready to run through `/spec`.
> **Scope:** How Phelix surfaces .NET-specific capabilities only when relevant,
> so the same harness can administer a Linux laptop *and* work inside a C# repo.
> **Key invariant:** `AgentLoop` stays unaware of all of this. Everything lives
> in the harness composition layer.

---

## 1. Concept grounding: shift-left in a harness

"Shift left" here is richer than the DevOps cliché (move testing earlier). In a
harness there are **two** distinct things to shift left:

- **Feedforward** — guides that *prevent* a problem before the model generates a
  token (AGENTS.md, skills, type info, API docs). The leftmost position there is.
- **Feedback** — sensors that *catch* a problem after generation (tests, linters,
  type-checks, review). Shifting these left = wiring them *inside* the agent loop
  for self-correction, before a human sees the diff.

Both axes also carry **functional** signal ("does it do the right thing?") and
**nonfunctional** signal ("is it secure / performant / architecturally sound?").
The mature move is to surface *both* early, while fixes are still cheap.

### Why this is Phelix's differentiator
**Roslyn is the best shift-left mechanism in the .NET ecosystem.** A TS harness
shells out to `tsc` and scrapes text. Phelix can hold a live compilation in
memory and hand the model structured, typed diagnostics the instant it writes a
file — compiler-grade feedback shifted maximally left, at near-linter speed.
Custom analyzers cover the nonfunctional axis too (architecture rules, security,
perf). **Portfolio headline:** *"a coding agent that gives the model
compiler-grade feedback the moment it acts."*

---

## 2. The core abstraction: Capability Pack

The naive framing ("a .NET mode vs a general mode") is a trap — mutually
exclusive modes duplicate the shared base (`read`/`write`/`list`/`bash`).

The architect's framing is **additive layers, not modes**:

```
  ┌─────────────────────────────────────────────┐
  │  .NET PACK   (additive, context-activated)   │
  │  • roslyn_diagnostics tool                   │
  │  • type-check sensor (loop backpressure)     │
  │  • ".NET workspace" feedforward guide        │
  ├─────────────────────────────────────────────┤
  │  BASE CAPABILITIES   (always on)             │
  │  • read / write / list / bash                │
  │  • session logging, retry, compaction        │
  └─────────────────────────────────────────────┘
        Linux laptop = base only
        C# repo       = base + .NET pack
```

A **Capability Pack** is a self-contained bundle of *everything that becomes true
when a context is active*, built from the three harness primitives:

- **tools** the agent can call
- **sensors** that run in the loop
- **feedforward** injected into the system prompt

The pack only ever *adds*. Base capabilities are never re-implemented.

---

## 3. Decisions locked in this session

| Area | Decision | Rationale |
|---|---|---|
| **Composition** | Additive layers, never mutually-exclusive modes | Avoids duplicating the shared base; packs only add |
| **Activation** | Hybrid: auto-detect (announced) + manual override flag | "Make the common case automatic and the rare case possible" — convention over configuration with an escape hatch |
| **Visibility** | Auto-activation must be *announced*, never silent | Implicit behavior trades predictability for convenience; announcing keeps it observable |
| **Precedence** | Explicit beats implicit — the user's flag always wins | The override exists precisely so the human can overrule the machine's guess |
| **Permissive force-on** | `--profile dotnet` with no `.csproj` → load anyway | Mechanism over policy; trust the dev's judgment |
| **Failure style** | Trust + **warn**, don't refuse and don't load silently | Diagnostic courtesy — the warning is the breadcrumb when Roslyn later returns nothing |
| **.NET idiom** | Keyed DI services + a `ProfileResolver` at startup | `AddKeyedSingleton<ITool>("dotnet", …)`; resolver picks which keys enter the registry. Idiomatic, senior-looking, good interview story |

### Activation precedence matrix
```
                    user says nothing   --profile dotnet     --profile none
                    (just runs phelix)  (force ON)           (force OFF)
  cwd HAS .csproj │ .NET pack loads    │ .NET pack loads    │ base only (flag wins)
  cwd NO  .csproj │ base only          │ load + WARN        │ base only
```

---

## 4. Transferable law surfaced

> **A permissive policy at the boundary creates a robustness requirement
> downstream.**

Letting the user force the .NET pack on with no project means
`roslyn_diagnostics` now *must* handle the "no `MSBuildWorkspace` / no compilation
loaded" state gracefully instead of throwing into the agent loop. Permissiveness
isn't free — it's a loan repaid in graceful-degradation code. (Strong thing to
name out loud in an interview.)

---

## 5. Open question (resolve before `/spec`)

**Is a pack a hardcoded C# class for now, or a discoverable thing
(a manifest / folder) from day one?**
This shapes the `ProfileResolver` interface and the pack data model — lock it in
early, per house practice.

---

## 6. Action items

- [ ] Decide the pack data model: hardcoded class vs discoverable manifest/folder
- [ ] Run this through `/spec` → `docs/decisions/capability-packs/spec.md`
- [ ] Spec the `ProfileResolver` contract (inputs: cwd scan result + override flag → output: active pack set)
- [ ] Spec the announce/warn UX strings (activation notice + force-on warning)
- [ ] Define graceful-degradation behavior for `roslyn_diagnostics` when no compilation is bound
- [ ] (Later) LinkedIn angle: "shift-left as a first-class harness primitive" + the Roslyn differentiator