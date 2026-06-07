# notes.md - phelix

Scratch pad for in-progress thinking. Organized work lives in ROADMAP.md and docs/decisions/.

---

## Active thoughts

- Conventions/Rules files with examples — look at how other harnesses handle per-project behavioral anchors (Gemini conversation: Agents Project)
- Custom exception handling — descriptive messages designed for agent consumption, not just humans
- Custom validation — same goal: errors the agent can act on without disambiguation
- Harness purpose framing: extract as much deterministic, verifiable behavior as possible and encode it as code-based rules. When all deterministic actions are coded, tune the system prompt.
- Session logs - option to rename or give user option to give name at the start of a session. easier to find in session folder. difficult with just date-time.

---

## Persistence layer

Still deciding on storage beyond JSONL session logs. Postgres feels like overkill for a personal dev tool. Lighter options worth evaluating before committing to a schema or store.

---

## Tool set notes

Current tools: `read_file`, `write_file`, `list_files`, `search_code`, `bash`. Specialized tools stay as path-validated, bounded defaults; bash handles everything else. Decision locked.

---

## Tool schema token cost — measured 2026-06-07

We instrumented `Program.cs` to log `turn.Usage.InputTokens` on the first turn with the prompt "What is your name?" against the default config (OpenRouter, qwen-flash).

**Result: 819 input tokens.**

Breakdown:
- System prompt (`"You are a helpful coding assistant."`) — ~7 tokens
- User message (`"What is your name?"`) — ~5 tokens
- Tool schemas (5 tools × ~160 tokens each) — **~807 tokens**

**98% of every turn's baseline cost is tool schema overhead.** This is not conversation history accumulation — it is a fixed floor paid on every single turn regardless of whether any tool is used, because `ChatOptions.Tools` is re-sent with every `GetStreamingResponseAsync` call.

This is a fundamental architectural problem. The current design registers all 5 tools at startup and keeps them registered for the session lifetime. At ~160 tokens per tool, every additional tool a developer adds costs ~160 tokens on every turn, forever.

**The fix is dynamic tool loading.** The agent should not have all tool schemas in context by default. It should have a lightweight catalog (name + one-liner) and a mechanism to load full schemas on demand when it decides it needs a tool. This is how most production harnesses work.

This is a prime candidate for a dedicated design phase. See the observations doc for the full framing. A spec needs to be written before any code is touched.
