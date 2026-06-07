# Config Layer — Feature Spec

**Status:** Approved  
**Phase:** MVP Blocker #3  
**Date:** 2026-06-06

---

## Problem

`ModelId` and `SystemPrompt` are hardcoded strings in `PhelixHost`. Switching models
requires a code change and a rebuild. There is no way to add a second provider, tune
`MaxTurns`, or adjust the system prompt without touching source.

The upcoming TUI needs to enumerate available models and providers, highlight the
active selection, and let the user switch without restarting. None of that is possible
without a structured config schema that the UI can bind to.

---

## Goal

Load a YAML config file at startup. Expose providers, named model profiles, and
session defaults as a strongly-typed C# object graph. Give `PhelixHost` an
`IConfigProvider` seam so the TUI can later swap in a live-reloading implementation
without touching the agent loop.

Config must be **optional** — if the file is absent, hardcoded defaults take over and
the app runs exactly as it does today.

---

## Non-Goals

- CLI flags to override individual config values — future phase
- Hot-reload during a live session — `IConfigProvider` enables it, but the implementation is out of scope here
- Provider credential management beyond `api_key_env` indirection — secrets stay in environment variables
- Multi-profile support (e.g. "work" vs "personal" configs) — future phase
- Validation UI or error recovery — malformed config throws a typed exception with a clear message and exits

---

## Schema

File location (resolved in order, first match wins):

1. `$PHELIX_CONFIG` environment variable
2. `~/.phelix/config.yaml`

If no file is found at either location, defaults are used and no error is raised.

### Full example

```yaml
active_model: claude-sonnet

system_prompt: "You are a helpful coding assistant."

providers:
  openrouter:
    api_key_env: OPENROUTER_API_KEY
    base_url: https://openrouter.ai/api/v1

models:
  claude-sonnet:
    provider: openrouter
    model_id: anthropic/claude-sonnet-4-6
    max_turns: 10
  qwen-flash:
    provider: openrouter
    model_id: qwen/qwen3.5-flash-02-23
    max_turns: 5
```

### Field reference

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `active_model` | string | No | first entry in `models` | Key into the `models` map |
| `system_prompt` | string | No | `"You are a helpful coding assistant."` | Injected at the start of every conversation |
| `providers` | map | No | built-in openrouter default | Named provider definitions |
| `providers.<name>.api_key_env` | string | Yes | — | Name of the env var holding the API key |
| `providers.<name>.base_url` | string | Yes | — | Base URL forwarded to the HTTP client |
| `models` | map | No | built-in qwen-flash default | Named model profiles |
| `models.<name>.provider` | string | Yes | — | Must match a key in `providers` |
| `models.<name>.model_id` | string | Yes | — | Provider-specific model identifier |
| `models.<name>.max_turns` | int | No | `5` | Overrides `AgentOptions.MaxTurns` for this model |

---

## C# Type Design

### Records

```
Phelix.Core/Config/
    PhelixConfig.cs       — root record
    ProviderConfig.cs     — per-provider record
    ModelConfig.cs        — per-model record
    IConfigProvider.cs    — single-method interface
    FileConfigProvider.cs — reads and deserializes the YAML file
    ConfigLoader.cs       — resolves path, calls provider, merges defaults
```

#### `PhelixConfig`

```csharp
public record PhelixConfig
{
    public string ActiveModel { get; init; }
    public string SystemPrompt { get; init; }
    public IReadOnlyDictionary<string, ProviderConfig> Providers { get; init; }
    public IReadOnlyDictionary<string, ModelConfig> Models { get; init; }
}
```

#### `ProviderConfig`

```csharp
public record ProviderConfig
{
    public string ApiKeyEnv { get; init; }
    public string BaseUrl { get; init; }
}
```

#### `ModelConfig`

```csharp
public record ModelConfig
{
    public string Provider { get; init; }
    public string ModelId { get; init; }
    public int MaxTurns { get; init; }
}
```

#### `IConfigProvider`

```csharp
public interface IConfigProvider
{
    PhelixConfig Load();
}
```

One method. The TUI's future `LiveConfigProvider` adds a `Changed` event on top —
that is not part of this interface and does not need to be anticipated here.

#### `FileConfigProvider`

Implements `IConfigProvider`. Takes a file path. Reads the YAML, deserializes into
`PhelixConfig`, returns it. Throws `ConfigException` (a new typed exception) if the
file exists but is malformed or references an unknown provider key.

#### `ConfigLoader`

Static class. `Load()` method:

1. Check `$PHELIX_CONFIG` → if set, use that path
2. Check `~/.phelix/config.yaml` → if present, use that path
3. If no file found, return `PhelixConfig.Default` (the hardcoded fallback)
4. Otherwise, call `FileConfigProvider.Load()` and validate cross-references
   (`active_model` must exist in `models`; each model's `provider` must exist in
   `providers`)

`ConfigLoader` owns the defaults so they live in exactly one place.

### Validation

On load, `ConfigLoader` checks:

- `active_model` key exists in `models` (if both are set)
- Each `models.<name>.provider` references a key that exists in `providers`
- `api_key_env` values resolve to non-empty environment variables (warning, not error)

Invalid config throws `ConfigException` with a message naming the offending key.
Missing file is silently ignored.

---

## YAML Parser

Use `YamlDotNet`. It is the de-facto standard for .NET YAML parsing, has no transitive
dependencies, and maps cleanly to records via its deserializer. Add to `Phelix.Core`
only — no other project needs a direct reference.

---

## Integration with `PhelixHost`

`PhelixHost.Build()` calls `ConfigLoader.Load()` to get a `PhelixConfig`. It then:

1. Looks up `config.Models[config.ActiveModel]` to get the active `ModelConfig`
2. Reads the API key from `Environment.GetEnvironmentVariable(provider.ApiKeyEnv)`
3. Constructs the `OpenAIClient` with `provider.BaseUrl`
4. Builds `AgentOptions` from `modelConfig.ModelId`, `config.SystemPrompt`, and
   `modelConfig.MaxTurns`

`PhelixHost` retains no hardcoded strings after this change.

---

## Files

### New

```
src/Phelix.Core/Config/PhelixConfig.cs
src/Phelix.Core/Config/ProviderConfig.cs
src/Phelix.Core/Config/ModelConfig.cs
src/Phelix.Core/Config/IConfigProvider.cs
src/Phelix.Core/Config/FileConfigProvider.cs
src/Phelix.Core/Config/ConfigLoader.cs
src/Phelix.Core/Config/ConfigException.cs
```

### Modified

```
src/Phelix.Cli/PhelixHost.cs   — call ConfigLoader, remove hardcoded strings
src/Phelix.Core/Phelix.Core.csproj — add YamlDotNet package reference
```

### New packages

| Package | Project | Purpose |
|---|---|---|
| `YamlDotNet` | `Phelix.Core` | YAML deserialization |

---

## Default values (no config file)

| Field | Default |
|---|---|
| `active_model` | `"qwen-flash"` |
| `system_prompt` | `"You are a helpful coding assistant."` |
| provider name | `"openrouter"` |
| `api_key_env` | `"OPENROUTER_API_KEY"` |
| `base_url` | `"https://openrouter.ai/api/v1"` |
| `model_id` | `"qwen/qwen3.5-flash-02-23"` |
| `max_turns` | `5` |

These match the current hardcoded values in `PhelixHost` exactly — no behavior change
when the config file is absent.

---

## TUI readiness

`PhelixConfig.Models` is an enumerable dictionary — the TUI can iterate it to build a
model picker list with no additional API surface. `ActiveModel` is the currently
selected key. Switching models in the TUI = updating `ActiveModel` and rebuilding
`AgentOptions`, which `IConfigProvider` cleanly enables via a future
`LiveConfigProvider` that emits a `Changed` event when the file is modified on disk.

---

## Implementation order

1. `ProviderConfig`, `ModelConfig`, `PhelixConfig` records — pure data, no logic
2. `ConfigException` — typed exception
3. `IConfigProvider` and `FileConfigProvider` — YAML deserialization
4. `ConfigLoader` — path resolution and default fallback
5. `PhelixHost` — wire up, remove hardcoded strings
6. Manual smoke test: run with no file (defaults), with a full file, with a bad file
7. `implementation-notes.md`

---

## Out of scope for this spec

- CLI `--model` flag to override `active_model` at runtime
- `LiveConfigProvider` with file-watcher for TUI hot-swap
- Validation of `base_url` format or reachability
- Support for providers other than OpenRouter-compatible APIs
