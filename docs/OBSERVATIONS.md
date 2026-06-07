# Model Behavior Observations

Dated notes on model behavior that could inform system prompt tuning.
Each entry records what was observed, why it matters, a candidate instruction, and the outcome once tested.

---

## 2026-06-07 — Tool schemas dominate input token cost

**Observed:** Instrumented the first turn with the prompt "What is your name?" and logged `turn.Usage.InputTokens`. Result: **819 input tokens**. The system prompt and user message together account for ~12 of those. The remaining ~807 are tool schema definitions sent via `ChatOptions.Tools` — 5 tools × ~160 tokens each.

**Why it matters:** This 807-token cost is not amortized. It is paid on every turn, whether or not the model calls a tool. As developers add tools to extend the harness, each addition raises the floor by ~160 tokens per turn, compounding across the entire session. A harness with 20 tools has a ~3,200 token fixed overhead before a single word of conversation history.

**Developer observation (Jose):** Most other harnesses do not load tools unless needed. For a harness intended to be extensible by third-party developers, this is a design-level problem — not a tuning problem. The agent working in this harness needs to know how to load its own tools when needed, how developers add tools, and how the harness instructs developers on usage. The fix needs to be dynamic and extensible by design.

**Our framing:** The right pattern is a two-layer tool system:
1. A **tool catalog** — always in context, cheap: just name + one-line description per tool. Lets the agent know what exists without paying for full schemas.
2. A **`load_tools` meta-tool** — always registered (small schema), the agent calls it with a list of tool names when it decides it needs them. Full schemas are injected only then.

This also unlocks developer extensibility naturally: third-party tools follow the same catalog/load convention, and the agent can discover and load them without the harness knowing about them at startup.

**Candidate instruction:** "Tools are not pre-loaded. Check the tool catalog to see what is available, then call `load_tools` with the names you need before using them."

**Outcome:** Not yet designed. A spec (`docs/decisions/dynamic-tool-loading/spec.md`) needs to be written before any implementation.

---

## 2026-06-07 — Redundant tool calls for directory listing

**Observed:** Model called `list_files` then `bash ls -la` to answer a single "what files are in my directory" prompt.
**Why it matters:** `list_files` returns recursive absolute paths for the entire tree — significantly higher token cost than a simple `ls`. Calling both tools to answer one question compounds the waste.
**Candidate instruction:** Prefer `bash` for directory listing. Do not re-verify results you already have from a prior tool call in the same turn.
**Outcome:** Untested.
