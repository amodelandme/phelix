# XML Documentation Coverage

**Status:** Approved  
**Phase:** Phase Queue  
**Date:** 2026-06-07

---

## Problem

XML documentation is missing or thin across the `Session`, `Config`, and `Agent`
namespaces. The tools namespace is well-documented; everything else is not.

This matters because the codebase's XML documentation is the primary way a
coding agent reads and understands the harness. A model landing on `TurnRecord`
or `PhelixConfig` with no docs has no way to know what the fields mean, why they
exist, or how they relate to the rest of the system. It will guess — and guess
wrong.

---

## Goal

Every public and internal type and member in `Phelix.Core` and `Phelix.Cli` has
XML documentation sufficient for an agent to understand its role, its
relationship to adjacent types, and any non-obvious constraints or invariants.

---

## Non-Goals

- Documenting private members (private helpers, private nested classes)
- Adding docs to `Program.cs` (top-level statements, no public API surface)
- Documentation on test projects

---

## Documentation standard

Every public/internal type needs:
- `<summary>` — one sentence: what it is
- `<remarks>` — why it exists, how it relates to the rest of the system, and any
  non-obvious invariants. This is the most important block for agent readability.

Every public/internal property or field needs:
- `<summary>` — what it holds and the unit or format where relevant

Every public/internal method needs:
- `<summary>` — what it does
- `<param>` for each parameter — what it is and any constraints
- `<returns>` — what comes back and what it means
- `<remarks>` — when the why is non-obvious

Enum values always need `<summary>`. The value name alone is not enough — a
model cannot infer what `Skipped` means for a sensor or whether `Failed` on a
tool call means an exception was thrown or an error string was returned.

---

## Files to update

### `src/Phelix.Core/Agent/Turn.cs`

`Turn` is the central artifact the loop produces. A model working in this
codebase will encounter it constantly. Document every property.

Key points to convey:
- `Messages` — the complete exchange including raw tool calls and results;
  used exclusively by `SessionLogger`; never passed back to the model
- `ContextMessages` — the pruned list passed as `conversationHistory` on the
  next `RunTurnAsync` call; tool exchange messages are stripped because the
  model already synthesized them into the final reply
- `Response` — the final model response for this turn (not intermediate
  tool-call responses)
- `Usage` — aggregate token counts across all inner model calls in this turn
- `ToolCalls` — ordered list of every tool invocation that occurred; each entry
  holds the truncated result the model actually saw
- `ExitReason` — why the loop stopped; `Completed` is normal; `TurnLimitReached`
  means `MaxTurns` was hit mid-tool-chain

---

### `src/Phelix.Core/Session/TurnRecord.cs`

`TurnRecord` is the durable log schema — intentionally different shape from
`Turn`. A model reading the session log needs to understand the mapping.

Key points to convey:
- `TurnRecord` vs `Turn`: `Turn` is the live runtime artifact; `TurnRecord` is
  what gets written to the JSONL session log. They are different shapes by
  design — `TurnRecord` stores only what is useful for observability, not the
  full message list.
- `FromTurn` — the only constructor path; documents what it copies and what it
  drops
- `FinalAssistantMessage` — the model's final text reply for this turn; not the
  raw tool responses
- `StartedAt` / `CompletedAt` — UTC; `CompletedAt` comes from `Turn.Timestamp`
  which is set at the moment the loop exits

---

### `src/Phelix.Core/Session/ToolCallRecord.cs`

Key points to convey:
- `CallId` — correlates this record back to the model's original tool-call
  request; assigned by the model, not Phelix
- `ArgumentsJson` — raw JSON-serialized arguments as the model supplied them
- `Result` — the truncated string the model actually received; capped at
  `AgentLoop.MaxToolOutputChars`; the full output is never stored
- `Status` — `Succeeded` means `ExecuteAsync` returned without throwing;
  `Failed` means no registered tool matched the name (the tool was never called)

---

### `src/Phelix.Core/Session/ToolCallStatus.cs`

- `Succeeded` — `ExecuteAsync` was called and returned a result string (which
  may itself be an error message from the tool)
- `Failed` — no tool with that name was registered; `ExecuteAsync` was never
  called; the model received an error notice

---

### `src/Phelix.Core/Session/SensorStatus.cs`

- `Passed` — the sensor ran and its check succeeded
- `Failed` — the sensor ran and its check failed (e.g. build errors, test
  failures)
- `Skipped` — the sensor was not applicable for this turn (e.g. no files were
  written, so the build sensor did not run)

---

### `src/Phelix.Core/Session/TurnEvent.cs`

Key points to convey:
- `TurnEvent` is the reserved extension point for Phase 3 sensor results; it is
  not yet populated by the harness
- The `[JsonPolymorphic]` attribute enables type-safe deserialization of derived
  event types from the session log
- `SensorResultEvent` is the only concrete type today; more sensor types will be
  added in Phase 3
- Do not use `TurnEvent` for anything other than sensor feedback — it is not a
  general event bus

---

### `src/Phelix.Core/Session/UsageSummary.cs`

- `InputTokens` — tokens sent to the model this turn, including all history and
  tool results; aggregated across all inner model calls (tool-call rounds + final
  response)
- `OutputTokens` — tokens generated by the model this turn; aggregated across
  all inner model calls

---

### `src/Phelix.Core/Session/SessionLogger.cs`

- `SessionId` — process-lifetime UUID; one value per Phelix run; shared across
  all turns in the session; used as the file name component and as the
  `sessionId` field in every `TurnRecord`

---

### `src/Phelix.Core/Config/IConfigProvider.cs`

Key points to convey:
- Seam between config loading and the rest of the harness; `FileConfigProvider`
  is the production implementation; tests or the TUI can supply alternatives
- `Load()` returns a fully validated `PhelixConfig`; implementations must throw
  `ConfigException` on invalid or missing config, never return partial state

---

### `src/Phelix.Core/Config/FileConfigProvider.cs`

Key points to convey:
- Reads `~/.phelix/config.yaml` via `ConfigLoader`; maps raw YAML types to
  domain records
- The `Raw*` nested classes are private deserialization targets; they are not
  part of the public contract and should not be used outside this file
- `Map()` performs all name resolution and validation; throws `ConfigException`
  on any invalid state

---

### `src/Phelix.Core/Config/ConfigLoader.cs`

Key points to convey:
- `Load()` is the single entry point for config; returns `PhelixConfig` with
  defaults filled when `~/.phelix/config.yaml` is absent
- `ResolveConfigPath()` — the path logic: `~/.phelix/config.yaml`, no override
  mechanism yet
- `Validate()` — what it checks and what causes a `ConfigException`
- `WarnMissingApiKeys()` — writes to `Console.Error`; does not throw

---

### `src/Phelix.Core/Config/ConfigException.cs`

- Thrown when config is invalid or required values are missing
- Not thrown for absent config files — absence falls back to defaults

---

### `src/Phelix.Core/Config/PhelixConfig.cs`

Key points to convey:
- The single config object passed through the harness at startup
- `Default` — the hardcoded fallback when no config file is present; useful to
  know what the defaults are
- `ActiveModel` — key into `Models`; must match a key in the map or
  `ConfigLoader` throws
- `SystemPrompt` — injected as `Instructions` on every `ChatOptions`; defines
  the agent's role and constraints for the session

---

### `src/Phelix.Core/Config/ModelConfig.cs`

Key points to convey:
- `Provider` — key into `PhelixConfig.Providers`; must match or config
  validation throws
- `ModelId` — passed verbatim to `IChatClient`; format is provider-specific
  (e.g. `anthropic/claude-sonnet-4-6` on OpenRouter)
- `MaxTurns` — per-model override for the tool-call turn limit; controls how
  many tool-call rounds the loop allows before halting with
  `TurnExitReason.TurnLimitReached`

---

### `src/Phelix.Core/Config/ProviderConfig.cs`

Key points to convey:
- `ApiKeyEnv` — the environment variable name that holds the API key; the key
  value is never stored in config; `PhelixHost` reads the env var at startup
- `BaseUrl` — the OpenAI-compatible endpoint; passed to `OpenAIClientOptions`

---

### `src/Phelix.Core/Telemetry/PhelixTelemetry.cs`

Thin docs on span and tag name constants. Each constant needs a `<summary>`
stating what it tracks and the unit/type of the value (e.g. `InputTokens` is
an `int` count of tokens, not bytes).

---

## Suggested approach for the implementing agent

Work file by file in this order:
1. `Turn.cs` — highest value, touched constantly
2. `TurnRecord.cs` — second most read, directly connected to `Turn`
3. `ToolCallRecord.cs`, `ToolCallStatus.cs`, `SensorStatus.cs`, `TurnEvent.cs`, `UsageSummary.cs`
4. `SessionLogger.cs` (just `SessionId`)
5. `PhelixConfig.cs`, `ModelConfig.cs`, `ProviderConfig.cs`
6. `IConfigProvider.cs`, `ConfigLoader.cs`, `FileConfigProvider.cs`, `ConfigException.cs`
7. `PhelixTelemetry.cs` (span/tag constants)

After each file: `dotnet build` to confirm no XML doc warnings were introduced.
Run the full test suite once at the end.
