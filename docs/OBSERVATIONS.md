# Model Behavior Observations

Dated notes on model behavior that could inform system prompt tuning.
Each entry records what was observed, why it matters, a candidate instruction, and the outcome once tested.

---

## 2026-06-07 — Redundant tool calls for directory listing

**Observed:** Model called `list_files` then `bash ls -la` to answer a single "what files are in my directory" prompt.
**Why it matters:** `list_files` returns recursive absolute paths for the entire tree — significantly higher token cost than a simple `ls`. Calling both tools to answer one question compounds the waste.
**Candidate instruction:** Prefer `bash` for directory listing. Do not re-verify results you already have from a prior tool call in the same turn.
**Outcome:** Untested.
