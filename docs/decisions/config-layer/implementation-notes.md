# Config Layer — Implementation Notes

**Date:** 2026-06-06  
**Branch:** feature/config-layer

---

## What shipped vs. the spec

Implemented exactly as specced. No deviations.

---

## YamlDotNet deserialization pattern

YamlDotNet cannot deserialize directly into records with `required` properties —
the deserializer needs a parameterless constructor and mutable setters. The solution
is a private `Raw*` class layer inside `FileConfigProvider` that acts as the
deserialization target, then a `Map()` step that constructs the immutable records.
This keeps the public API fully immutable while satisfying YamlDotNet's requirements.

---

## `api_key_env` lookup moved to `PhelixHost`

The spec placed API key resolution in `PhelixHost`, not in `ConfigLoader` or
`FileConfigProvider`. This is intentional — config loading is pure data work;
resolving environment variables is a runtime concern. `ConfigLoader` only warns
about missing keys; `PhelixHost` throws if the active provider's key is absent.

---

## `~/.phelix/config.yaml` sample file

A sample config was written to `~/.phelix/config.yaml` with two model profiles
(`claude-sonnet` and `qwen-flash`). Switching models = change `active_model` in
that file. No rebuild required.
