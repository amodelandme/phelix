# Implementation Notes: Codebase Audit & Hardening

## Path containment fix (`IsWithinRoot`)

All three file-system tools — `ReadFileTool`, `WriteFileTool`, `BashTool` — were using
`absolutePath.StartsWith(RootDirectory, StringComparison.Ordinal)` to confine I/O to the
project root. This passes for any path whose string *prefix* matches, including sibling
directories like `/home/user/project-evil` when the root is `/home/user/project`.

The fix is a shared static helper on `ReadFileTool`:

```csharp
internal static bool IsWithinRoot(string root, string candidate)
{
    string relative = Path.GetRelativePath(root, candidate);
    return !relative.StartsWith("..") && !Path.IsPathRooted(relative);
}
```

`Path.GetRelativePath` resolves the candidate relative to the root. If the result starts
with `..`, the candidate is above the root. If it is rooted (a different drive on Windows
or an absolute path that shares no prefix), it is outside entirely. Both conditions
together are complete.

`..` traversal attacks (e.g. `/root/subdir/../../etc/passwd`) were already blocked by
the existing `Path.GetFullPath` call that runs before the check — `GetFullPath`
canonicalizes all `..` segments. The vulnerability was purely the missing
directory-boundary check on sibling paths.

`WriteFileTool` and `BashTool` call `ReadFileTool.IsWithinRoot` rather than duplicating
the logic. The helper is `internal` — not part of the public `ITool` contract.

## ANSI spoofing protection (`ControlCharSanitizer`)

`InteractiveApprovalGate` prints two model-controlled strings before asking for approval:
`toolName` and `callSummary`. A prompt-injected model response can embed ANSI escape
sequences or bare control characters (`\r`, `\b`, `\x1b[1A`) that reposition the cursor
and overwrite previously rendered lines — the user sees a benign description but approves
the real, hidden command.

`ControlCharSanitizer.Sanitize` replaces every control character with a visible
angle-bracketed name before the string reaches any `TextWriter`:

- C0 range (U+0000–U+001F): named replacements (`<ESC>`, `<CR>`, `<BS>`, etc.). `\t` and
  `\n` are left intact — their terminal behaviour is unambiguous and predictable.
- U+007F: `<DEL>`.
- C1 range (U+0080–U+009F): `<U+XXXX>`.
- U+2028 / U+2029 (Unicode line/paragraph separators): `<LS>` / `<PS>`. The C# compiler
  treats these as line terminators, making them viable spoofing vectors. They are matched
  as `(char)0x2028` / `(char)0x2029` in the switch — bare char literals for these
  codepoints cause a compiler error because they are parsed as newlines.

The implementation uses a lazy `StringBuilder`: if no control characters are found (the
common case for benign input), it returns the original string reference with zero
allocation.

## Why `ControlCharSanitizer` lives in `Phelix.Core.Agent`

The sanitizer operates on `string → string` with no terminal or I/O dependency. An early
draft put it in `Phelix.Tui.TerminalRenderer`, but that would require either a reverse
dependency (`Phelix.Core` → `Phelix.Tui`) or injecting the sanitizer as a delegate into
`InteractiveApprovalGate`. Both are worse than placing it alongside the gate in
`Phelix.Core.Agent` where it is consumed.

## `SqliteCommand` disposal and per-iteration allocation

`SqliteSessionStore.AppendAsync` was creating a new `SqliteCommand` inside the
`tool_outputs` foreach loop — one undisposed command object per tool call per turn. The
`insertTurn` command and both read-path commands in `GetTurnsAsync` and
`SearchToolOutputsAsync` were also never disposed.

The fix creates the `tool_outputs` command once, binds named `SqliteParameter` references,
and reassigns `.Value` per iteration:

```csharp
await using SqliteCommand insertOutput = _connection.CreateCommand();
// ... set CommandText once ...
SqliteParameter pToolName = insertOutput.Parameters.Add("$toolName", SqliteType.Text);
// ...
foreach (ToolCallRecord toolCall in record.ToolCalls)
{
    pToolName.Value = toolCall.Name;
    // ...
    await insertOutput.ExecuteNonQueryAsync(cancellationToken);
}
```

All four commands in the file are now `await using`. The loop is guarded with
`if (record.ToolCalls.Count > 0)` to skip command construction entirely on turns with no
tool calls.

## Collection expression spread in `AgentLoop`

```csharp
// Before
List<ChatMessage> messages = new List<ChatMessage>(conversationHistory)
{
    new(ChatRole.User, userMessage)
};

// After
List<ChatMessage> messages = [.. conversationHistory, new(ChatRole.User, userMessage)];
```

`new List<T>(IEnumerable<T>)` sets capacity to the source count, so appending one element
immediately triggers a resize. The C# 14 spread operator queries `.Count` on the source
(which implements `ICollection<T>`) and allocates at `count + 1` in a single operation.

`toolResults` inside the inner dispatch loop gains an explicit capacity hint from
`assistantMessage.Contents.Count` — knowable before iteration begins.

## LINQ removal in `TokenThresholdPolicy`

`ShouldCompact` called `.Sum(EstimateTokens)` where `EstimateTokens` itself called
`.OfType<TextContent>().Sum(...)`. `.OfType<T>()` allocates an enumerator on every
message on every turn. The replacement is two nested `foreach` loops with a C# 14
property pattern for the null check:

```csharp
if (content is TextContent { Text: { } text })
    totalChars += text.Length;
```

Semantically identical, zero allocations.

## Streaming retry buffer capacity

`RetryingChatClient.GetStreamingResponseAsync` initialised the per-attempt buffer as
`List<ChatResponseUpdate> buffer = []` — default capacity 4. A typical response produces
64–256 chunks, causing 4–6 internal resize cycles. The buffer is now `new(capacity: 128)`.

The buffer's role as a transaction boundary is intentional and correct: tokens reach the
terminal only after a full response has been verified. If a transient failure occurs
mid-stream, the partial buffer is discarded and the request retries from scratch — the UI
never shows a chimera of two different model generations.

Replacing this with an `HttpClient`-level resilience pipeline
(`Microsoft.Extensions.Resilience`) is the right long-term move but requires introducing
`IServiceCollection` into the composition root. The capacity hint is the appropriate
optimisation at the current architectural scope.

## `UsageSummary` as `readonly record struct`

`UsageSummary` holds two `int` fields and is created once per turn. As a `record class`
it is heap-allocated. As a `readonly record struct` it fits in two registers with no GC
overhead. The change is a one-word addition and has no effect on the call sites — `record
struct` retains value equality semantics.
