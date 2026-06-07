# notes.md - phelix

Scratch pad for in-progress thinking. Organized work lives in ROADMAP.md and docs/decisions/.

---

## Active thoughts

- Conventions/Rules files with examples — look at how other harnesses handle per-project behavioral anchors (Gemini conversation: Agents Project)
- Custom exception handling — descriptive messages designed for agent consumption, not just humans
- Custom validation — same goal: errors the agent can act on without disambiguation
- Harness purpose framing: extract as much deterministic, verifiable behavior as possible and encode it as code-based rules. When all deterministic actions are coded, tune the system prompt.

---

## Persistence layer

Still deciding on storage beyond JSONL session logs. Postgres feels like overkill for a personal dev tool. Lighter options worth evaluating before committing to a schema or store.

---

## Tool set notes

Current tools: `read_file`, `write_file`, `list_files`, `search_code`, `bash`. Specialized tools stay as path-validated, bounded defaults; bash handles everything else. Decision locked.
