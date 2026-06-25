# Phelix Audit — C# 14 Idiom, Hot-Loop Performance & Storage Integrity

**Date:** 2026-06-25
**Scope:** `src/Phelix.Core` and `src/Phelix.Cli` (excluding `bin/`, `obj/`).
**Type:** Read-only exploratory audit. **No code was changed.** Every cited snippet was
read verbatim from source before inclusion.
**Pillars:** (1) Idiomatic C# 14 & elegance · (2) GC pressure in the hyper-iteration
loop · (3) Dual-store (JSONL + SQLite/FTS5) integrity.

---

## Verdict

The codebase is **already strongly idiomatic and allocation-conscious.** Primary
constructors, `switch` expressions, `readonly record struct` for `UsageSummary`,
pre-sized buffers, a capacity-hinted retry buffer, and zero-allocation `foreach` loops
in the compaction policy are all present and correct. The prior hardening pass
(`docs/audit-and-hardening-2026-06-07.md`) clearly landed.

Findings are therefore **targeted, not systemic.** Exactly one rises to WARNING — an
undisposed-handle regression that also contradicts a written claim in the prior audit.
Everything else is LOW or TRIVIAL polish, plus one architectural recommendation tied to
the stated "fork-and-resume" goal. Two tempting "fixes" surfaced during the sweep are
**rejected** below because acting on them would break correctness.

---

## Severity Matrix

| ID | Sev | Pillar | File : Line | Root cause | Fix sketch |
|----|-----|--------|-------------|------------|------------|
| **S-1** | **WARNING** | Storage | `SqliteSessionStore.cs:252,271` | Two `SqliteCommand`s created, never disposed | `using SqliteCommand …` |
| **S-2** | NOTE | Storage | `PhelixSession.cs:37` | `IReadOnlyList` over a mutable `List`, not truly immutable | `ImmutableArray<ChatMessage>` for fork-and-resume |
| **P-1** | LOW–MED | Perf | `AgentLoop.cs:296` | `.All(c => …)` closure + enumerator per assistant msg, per turn | manual `foreach` early-exit |
| **P-2** | LOW* | Perf | `AgentLoop.cs:137-139` | Defensive copy of `call.Arguments` per tool call | optional — pass through if trusted |
| **P-3** | LOW | Perf | `StreamingMarkdownWriter.cs:14` | `StringBuilder` with no capacity hint, resizes per response | `new(capacity: 4096)` |
| **P-4** | LOW | Perf (display) | `CliRenderer.cs:210-217` | LINQ `.Select` + `string.Join` per tool-start event | `StringBuilder` loop |
| **P-5** | TRIVIAL | Perf | `AgentLoop.cs:187` | Empty `Dictionary` allocated on cold error path | shared empty singleton |
| **C-1** | LOW | Idiom | `ReadFileTool.cs:27-30`, `ListFilesTool.cs:42-46` | Assign-only ctors | primary constructor |
| **C-2** | TRIVIAL | Idiom | `ListFilesTool.cs:17-18` | `HashSet` initializer | collection expression with comparer |

\* P-2 downgraded from the initial "Medium-High" after verification — see detail.

---

## Pillar 3 — Storage & Session Integrity

### S-1 (WARNING) — Undisposed `SqliteCommand` in `EnsureSchema()`

**File:** `src/Phelix.Core/Session/SqliteSessionStore.cs:250-282`

```csharp
void EnsureSchema()
{
    SqliteCommand createTurns = _connection.CreateCommand();   // line 252 — no using
    createTurns.CommandText = """ CREATE TABLE IF NOT EXISTS turns ( … ) """;
    createTurns.ExecuteNonQuery();

    SqliteCommand createToolOutputs = _connection.CreateCommand();   // line 271 — no using
    createToolOutputs.CommandText = """ CREATE VIRTUAL TABLE IF NOT EXISTS tool_outputs USING fts5( … ) """;
    createToolOutputs.ExecuteNonQuery();
}
```

**Root cause.** Both commands are created with `_connection.CreateCommand()` and never
disposed. `EnsureSchema()` runs once per store construction, so the leak is bounded (two
handles per session-store instance) — but in WAL mode, lingering command/statement
handles are exactly the kind of object that can keep a `sqlite3_stmt` finalization
pending and interfere with checkpoint cleanup. It is a genuine undisposed-handle defect.

**Regression note.** The prior audit
(`docs/audit-and-hardening-2026-06-07.md`, finding **W-2**, line 193) states:

> "All four commands in `SqliteSessionStore` are now `await using`."

That count covered `AppendAsync`'s turn-insert and tool-output-insert,
`GetTurnsAsync`, and `SearchToolOutputsAsync` — and those **are** correctly fixed (see
Confirmed-Correct inventory). It never included the two `EnsureSchema()` commands. The
written claim is therefore inaccurate by omission, and these two sites remain unfixed.

**Fix (C# 14).** `using` declarations — no other change needed:

```csharp
void EnsureSchema()
{
    using SqliteCommand createTurns = _connection.CreateCommand();
    createTurns.CommandText = """ … """;
    createTurns.ExecuteNonQuery();

    using SqliteCommand createToolOutputs = _connection.CreateCommand();
    createToolOutputs.CommandText = """ … """;
    createToolOutputs.ExecuteNonQuery();
}
```

`EnsureSchema()` is synchronous, so plain `using` (not `await using`) is correct and
sufficient here.

---

### S-2 (DESIGN NOTE) — `ConversationHistory` is read-only-typed, not immutable

**File:** `src/Phelix.Core/Session/PhelixSession.cs:37`

```csharp
public IReadOnlyList<ChatMessage> ConversationHistory { get; private set; } = [];
```

The runtime value is a concrete, mutable `List<ChatMessage>` produced by
`AgentLoop.BuildContextMessages` (`AgentLoop.cs:302`, `return contextMessages;`). It is
exposed through an `IReadOnlyList<>` *view* and replaced by whole-field reassignment on
each turn (`PhelixSession.cs:89`) and on compaction (`PhelixSession.cs:121-124`).

**This is safe today** — the setter is private, no code mutates the list in place, and
each turn swaps the whole reference. It is **not a defect.** But the stated
"fork-and-resume" goal wants more than write-safety: forking a session means holding two
independent history references that must never alias. An `IReadOnlyList<>` over a `List`
gives no structural-sharing guarantee and can be downcast to `List<ChatMessage>` by any
consumer that knows the concrete type.

**Recommendation (only if fork-and-resume is built).** Make the field
`ImmutableArray<ChatMessage>`. `ImmutableArray<T>` is itself a `readonly struct` wrapping
a single array — near-zero overhead for read, structurally shareable across forks, and
impossible to mutate or downcast. `BuildContextMessages` would return
`ImmutableArray<ChatMessage>` (build with a pre-sized `ImmutableArray.CreateBuilder`),
and the compaction reassignment becomes `[summaryMessage]` unchanged. Defer until the
fork feature actually lands — there is no value in the change before then.

---

## Pillar 2 — Hot-Loop Performance

### P-1 (LOW–MED) — `.All()` closure per assistant message, per turn

**File:** `src/Phelix.Core/Agent/AgentLoop.cs:285-303` (method `BuildContextMessages`)

```csharp
if (message.Role == ChatRole.Assistant &&
    message.Contents.Count > 0 &&
    message.Contents.All(c => c is FunctionCallContent))   // line 296
    continue;
```

`Enumerable.All` allocates an enumerator, and the lambda `c => c is FunctionCallContent`
is a closure-free static-capturable predicate but is still dispatched through a delegate.
`BuildContextMessages` runs **every turn**, iterating the full history, so this fires
once per assistant message — cost grows linearly with conversation length.

**Fix (zero-allocation `foreach`):**

```csharp
if (message.Role == ChatRole.Assistant && message.Contents.Count > 0)
{
    bool allCalls = true;
    foreach (AIContent c in message.Contents)
    {
        if (c is not FunctionCallContent) { allCalls = false; break; }
    }
    if (allCalls) continue;
}
```

`List<T>.Enumerator` is a struct, so the `foreach` allocates nothing and the early
`break` short-circuits — strictly cheaper than `.All()`.

---

### P-2 (LOW, optional — downgraded) — defensive copy of `call.Arguments`

**File:** `src/Phelix.Core/Agent/AgentLoop.cs:137-139`

```csharp
IReadOnlyDictionary<string, object?> args = call.Arguments is not null
    ? new Dictionary<string, object?>(call.Arguments, StringComparer.Ordinal)
    : [];
```

This copies the model-supplied argument dictionary once per tool call (a warm path —
multiple calls per turn in agentic sessions). The initial sweep flagged it as
Medium-High; on review it is **deliberate and defensible**: `args` is then handed to the
approval gate *and* the tool, and the copy with `StringComparer.Ordinal` both snapshots
model-controlled input and normalizes key comparison. Removing it trades that isolation
for a single allocation.

**Recommendation: leave as-is unless profiling proves it matters.** If it does, and
`call.Arguments` is already an `IReadOnlyDictionary<string, object?>` with ordinal
comparison, pass it through directly. Do not "fix" this blindly — the copy is a safety
boundary, not an oversight. Listed only for completeness.

---

### P-3 (LOW) — `StringBuilder` without capacity hint

**File:** `src/Phelix.Cli/StreamingMarkdownWriter.cs:14`

```csharp
readonly StringBuilder buffer = new();
```

Constructed once per turn, accumulates a full streamed response (commonly 1–5 KB) before
flushing. Default `StringBuilder` capacity is 16 chars, so a typical response triggers
several internal chunk reallocations as it grows.

**Fix:** `readonly StringBuilder buffer = new(capacity: 4096);` — sizes for the common
case in one allocation; the builder still grows for larger responses. Cheap, contained,
and the type is reused across turns via `Clear()`, so the hint pays off repeatedly.

---

### P-4 (LOW, display path) — LINQ per tool-start event

**File:** `src/Phelix.Cli/CliRenderer.cs:205-218` (method `BuildArgList`)

```csharp
IEnumerable<string> pairs = args.Select(kvp =>
{
    string raw   = kvp.Value?.ToString() ?? "null";
    string value = raw.Length > 60 ? raw[..60] + "…" : raw;
    return $"{Markup.Escape(kvp.Key)}={Markup.Escape(value)}";
});

return " " + string.Join(" ", pairs);
```

`.Select` allocates a lazy enumerable + delegate; `string.Join` enumerates it. Fires on
every tool-start render. This is the **display layer**, not the core loop, so impact is
modest — but it is a trivially avoidable per-event allocation.

**Fix:** build into a `StringBuilder` directly:

```csharp
StringBuilder sb = new(capacity: args.Count * 24);
foreach (KeyValuePair<string, object?> kvp in args)
{
    string raw   = kvp.Value?.ToString() ?? "null";
    string value = raw.Length > 60 ? raw[..60] + "…" : raw;
    sb.Append(' ').Append(Markup.Escape(kvp.Key)).Append('=').Append(Markup.Escape(value));
}
return sb.ToString();
```

---

### P-5 (TRIVIAL) — empty `Dictionary` on the unregistered-tool path

**File:** `src/Phelix.Core/Agent/AgentLoop.cs:187`

```csharp
await onFailedStart(call.Name, new Dictionary<string, object?>(StringComparer.Ordinal));
```

Allocates an empty dictionary purely to satisfy the callback signature, on the
unregistered-tool error path (cold). Replace with a shared
`static readonly IReadOnlyDictionary<string, object?> EmptyArgs =
    new Dictionary<string, object?>(StringComparer.Ordinal);` (or
`ReadOnlyDictionary<…>.Empty` equivalent) and pass that. Negligible impact; listed for
completeness only.

---

## Pillar 1 — Idiomatic C# 14 & Elegance

The sweep found **no** `new List<T>()` + `.Add()` blocks and **no** stray
`.ToList()` / `.ToArray()` chains in core paths — collection-expression hygiene is
already good, and `[]` / spread are used where appropriate. Remaining items are minor.

### C-1 (LOW) — assign-only constructors → primary constructors

**File:** `src/Phelix.Core/Tools/ReadFileTool.cs:27-30`

```csharp
public ReadFileTool(string? rootDirectory = null)
{
    RootDirectory = Path.GetFullPath(rootDirectory ?? Directory.GetCurrentDirectory());
}
```

→

```csharp
public class ReadFileTool(string? rootDirectory = null) : ITool
{
    public string RootDirectory { get; } =
        Path.GetFullPath(rootDirectory ?? Directory.GetCurrentDirectory());
    // …
}
```

**File:** `src/Phelix.Core/Tools/ListFilesTool.cs:42-46`

```csharp
public ListFilesTool(string? rootDirectory = null, IReadOnlySet<string>? excludedDirectories = null)
{
    RootDirectory = Path.GetFullPath(rootDirectory ?? Directory.GetCurrentDirectory());
    ExcludedDirectories = excludedDirectories ?? DefaultExcludedDirectories;
}
```

→ primary constructor with property initializers:

```csharp
public class ListFilesTool(
    string? rootDirectory = null,
    IReadOnlySet<string>? excludedDirectories = null) : ITool
{
    public string RootDirectory { get; } =
        Path.GetFullPath(rootDirectory ?? Directory.GetCurrentDirectory());
    public IReadOnlySet<string> ExcludedDirectories { get; } =
        excludedDirectories ?? DefaultExcludedDirectories;
    // …
}
```

Pure visual-clutter reduction — the get-only properties stay; only the ceremony goes.

### C-2 (TRIVIAL) — `HashSet` initializer → collection expression

**File:** `src/Phelix.Core/Tools/ListFilesTool.cs:17-18`

```csharp
static readonly IReadOnlySet<string> DefaultExcludedDirectories =
    new HashSet<string>(StringComparer.Ordinal) { ".git", "bin", "obj" };
```

A collection expression cannot carry the `StringComparer.Ordinal` constructor argument,
so this is a wash, not a clear win — the comparer matters for case-sensitive segment
matching. **Recommend leaving as-is**; flagged only so a future reader does not "simplify"
it to `[".git", "bin", "obj"]` and silently drop the ordinal comparer. (For an explicitly
ordinal set you would still need the `new HashSet<string>(StringComparer.Ordinal) { … }`
form.)

---

## Verified-Correct Inventory

Confirmed by direct reading — these are already optimal; do not "fix" them:

- **`UsageSummary`** (`Session/UsageSummary.cs:10`) — already
  `readonly record struct UsageSummary(int InputTokens, int OutputTokens)`. Two ints,
  register-friendly, no heap allocation per turn. ✓
- **Retry buffer** (`Agent/RetryingChatClient.cs`) — streamed updates buffered into
  `List<ChatResponseUpdate>(capacity: 128)`; capacity hint present, buffer discarded only
  on (rare) retry. ✓
- **`BuildContextMessages` list** (`Agent/AgentLoop.cs:287`) — pre-sized to
  `messages.Count`, no resize during fill. ✓
- **`TokenThresholdPolicy.ShouldCompact`** (`Session/TokenThresholdPolicy.cs`) — nested
  `foreach` over messages/contents, zero LINQ, zero allocation. ✓
- **SQLite write/read commands** in `AppendAsync`, `GetTurnsAsync`,
  `SearchToolOutputsAsync` — all `await using`, parameters added once and **rebound**
  (not reallocated) per row. The W-2 fix is genuine for these four. ✓
- **FTS5 insert** — single command reused with rebound parameters across tool-output
  rows; no per-row parameter churn. ✓

---

## Rejected Findings

Surfaced during the sweep, **rejected after verification** — acting on these would break
correctness. Documented so they are not re-proposed later.

### ✗ `TurnEvent` / `SensorResultEvent` → `readonly record struct`

**File:** `src/Phelix.Core/Session/TurnEvent.cs:18-34`

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SensorResultEvent), "sensorResult")]
public abstract record TurnEvent(DateTimeOffset Timestamp);

public sealed record SensorResultEvent(
    DateTimeOffset Timestamp, string SensorName, string Output, SensorStatus Status
) : TurnEvent(Timestamp);
```

**Why rejected — two independent reasons:**

1. **Polymorphism requires reference types.** `[JsonPolymorphic]` +
   `[JsonDerivedType]` dispatch on an `abstract` base. Value types cannot be `abstract`,
   cannot participate in a derived-type hierarchy, and `System.Text.Json` polymorphic
   serialization does not apply to structs. Converting to `readonly record struct` breaks
   the serialization contract outright.
2. **Not a hot type.** The class's own XML doc states it is a *reserved, not-yet-populated
   Phase 3 extension point* — "no code currently appends events to a turn record." There
   is no allocation to optimize because nothing allocates it yet.

**Leave as `record`.** The reference-type choice is correct and deliberate.

---

## How to act on this report

Priority order if any change is made later (none made here):

1. **S-1** — the only WARNING; a one-line-each `using` fix, and it corrects a false claim
   in the prior audit doc.
2. **P-1, P-3** — cheap, contained, real hot-path / per-turn wins.
3. **C-1** — pure elegance; do alongside other edits to those files.
4. Everything else — TRIVIAL / optional; not worth a dedicated change.

Do **not** touch the Verified-Correct or Rejected items.
