# notes.md - phelix

- TUI design reference: OpenCode (Go/Charm stack). Two-panel layout — left ~75% conversation, right ~25% persistent sidebar (task title, context/token stats, MCP connections, task checklist with live checkmarks). Intro screen: pure black, centered wordmark, single focused input with blue left-border accent, status line at bottom. Near-monochrome palette, single accent color used sparingly. Target implementation: Spectre.Console. Do cleanup and MVP validation before starting TUI work.

- MaxTurns cut-off produces a blank response — no feedback to the user that the turn limit was hit. Fix 1: print `[turn limit reached]` in Program.cs when `Response.FinishReason == ToolCalls` after the turn returns. Fix 2 (bigger): stream tool-call progress so the user sees activity during multi-step turns.

These are personal notes for the developer. Scan these occasionally for ideas that I'm thinking about:

- review persistence layer decisions (postegres maybe overkill). Look at lighter options
- initial tool set: read, write, bash, doc search (context7? local storage? How to store?)
- Conventions/Rules files with examples (Gemini conversation: Agents Project)
- Code Cleanup:
  - Magic strings
- Custom Exception handling - Descriptive for agent use
- Custom Validation - Descriptive for agent use
- SessionEntry schema redesign: currently captures only user prompt + assistant text. Tool calls (name, arguments, result) are silently dropped. For RAG and observability this schema is already incomplete. Needs a dedicated spec — design with both RAG and observability in mind before touching the persistence layer. Do this after pre-TUI cleanup lands.

- Tool consolidation: decided to keep read_file, write_file, list_files, search_code alongside bash. Bash added as BashTool. Specialized tools stay as path-validated, bounded defaults; bash handles everything else.
- Config layer: model ID and system prompt are hardcoded in Program.cs. Swapping models requires a code change. Need a config layer (YAML?) so the harness is usable without editing source. Deferred — write spec in docs/decisions/ first.
- Resilience: no retry or circuit breaker. A 429 from OpenRouter kills the session. Need retry/backoff middleware wired into ChatClientBuilder. Deferred — write spec in docs/decisions/ first.
- agent diagnostics? - agent (or separate specialized agent?) will analyze the (entire?) session, noting where mistakes where made, blockages hit, confusion in instructions, lack of clarity, how many times did the agent change its mind based on new information it discovered or searched for that should have been known in the first place, 
- More narrow focus of above point: how can we create alerts when models attempt to use a tool or carry out a task unsuccessfully multiple times only to have it finally search for the solution and solve it then immediately. And then how can we help/guide the model to search first before use if necessary?
- Caching frequently used commands?? Avoid having to run search/bash.
- Good phrasing: Harness purpose: extract as much dterministic, verifiable actions as possible and provide them as code-based, deterministic rules. When all code-based, deterministic possible actions an agent can take have been coded, tweak/modify your system prompt.
- Make it so that the agent cannot lie. 
- future ui: left panel- conversation, right pane- code with ability to highlight and ask ai questions about highlighted text; edit text on the spot with ai responding to changes; in step by step specs, agent can display code examples as it explains sections of the spec, inviting the user's comments and reading user's changes and responding; syntax-highlighting for code; displays text portion of spec, then switches to code-view so that user can inspect possible implmentation details, then back to next section of the spec.
- voice communication

---

## Feature Discussions

### Session file formatting (2026-06-04)
JSONL (compact, one entry per line) is the right on-disk format — prettifying adds size with no benefit. Human readability is a tooling concern (`jq`), not a storage concern. The real question is schema: `SessionEntry` needs to capture user intent, clean assistant text, tool calls + results as discrete facts, timestamps, and session ID to be RAG-ready. That schema decision is worth making before a persistence layer is built. Do not change the format; design the schema with RAG in mind. (Or do we design with OBSERVABILITY in mind!!)

### Streaming / blank screen while waiting (2026-06-04)
Streaming is already wired: `AgentLoop` calls `GetStreamingResponseAsync` and fires `onChunk` per chunk; `Program.cs` passes `TerminalRenderer.WriteChunk`. The blank screen is a first-token latency problem (reasoning models think before they speak) not a missing feature. Fix: add a spinner or "thinking…" indicator that runs from prompt submission until the first chunk arrives. This is a TUI concern.

### TUI design (2026-06-04)
The Charm stack (Bubble Tea + Lip Gloss) is Go-only. Pulling it into a .NET project means either shipping a Go binary with IPC or rewriting the CLI in Go — both defeat the purpose of this project. Use Spectre.Console instead: layout primitives, styled text, live renderables (spinners, progress bars, tables), ANSI abstraction with graceful degradation. Study the Charm stack's design philosophy and UX ideas as inspiration, implement them in .NET with Spectre.Console.