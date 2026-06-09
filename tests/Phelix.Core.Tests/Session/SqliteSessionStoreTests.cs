using Microsoft.Extensions.AI;
using Phelix.Core.Agent;
using Phelix.Core.Session;

namespace Phelix.Core.Tests.Session;

public class SqliteSessionStoreTests : IDisposable
{
    readonly SqliteSessionStore _store;

    public SqliteSessionStoreTests()
    {
        _store = new SqliteSessionStore(new SessionContext(":memory:", null, DateTimeOffset.UtcNow));
    }

    public void Dispose() => _store.Dispose();

    // ── helpers ──────────────────────────────────────────────────────────────

    static TurnRecord BuildRecord(
        string sessionId = "session-a",
        string turnId = "turn-01",
        IReadOnlyList<ToolCallRecord>? toolCalls = null)
    {
        ChatMessage userMessage = new(ChatRole.User, "do something");
        ChatMessage assistantMessage = new(ChatRole.Assistant, "done");
        ChatResponse response = new([assistantMessage]) { ModelId = "fake-model" };
        IReadOnlyList<ChatMessage> messages = [userMessage, assistantMessage];

        Turn turn = new(
            Messages: messages,
            ContextMessages: messages,
            Response: response,
            Timestamp: DateTimeOffset.UtcNow,
            Usage: new UsageSummary(100, 50),
            ToolCalls: toolCalls ?? [],
            ExitReason: TurnExitReason.Completed
        );

        return TurnRecord.FromTurn(
            turn,
            context: new SessionContext(sessionId, null, DateTimeOffset.UtcNow),
            userMessage: "do something",
            turnId: turnId,
            startedAt: DateTimeOffset.UtcNow.AddSeconds(-1)
        );
    }

    static ToolCallRecord BuildToolCall(string name = "read_file", string result = "file content") =>
        new(
            CallId: Guid.NewGuid().ToString("N"),
            Name: name,
            ArgumentsJson: $"{{\"path\":\"src/Program.cs\"}}",
            Result: result,
            Status: ToolCallStatus.Succeeded
        );

    // ── AppendAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AppendAsync_WritesRowToTurnsTable()
    {
        TurnRecord record = BuildRecord(turnId: "turn-01");
        await _store.AppendAsync(record);

        IReadOnlyList<TurnRecord> turns = await _store.GetTurnsAsync("session-a");

        Assert.Single(turns);
        Assert.Equal("turn-01", turns[0].TurnId);
        Assert.Equal("session-a", turns[0].SessionId);
    }

    [Fact]
    public async Task AppendAsync_WritesRowsToToolOutputsTable_OnePerToolCall()
    {
        IReadOnlyList<ToolCallRecord> toolCalls =
        [
            BuildToolCall("read_file", "content of file A"),
            BuildToolCall("bash", "exit code 0")
        ];

        TurnRecord record = BuildRecord(toolCalls: toolCalls);
        await _store.AppendAsync(record);

        IReadOnlyList<ToolCallRecord> results = await _store.SearchToolOutputsAsync("content");

        Assert.Single(results);
        Assert.Equal("read_file", results[0].Name);
    }

    [Fact]
    public async Task AppendAsync_IsAtomic_NeitherTableWrittenOnFailure()
    {
        IReadOnlyList<ToolCallRecord> toolCalls = [BuildToolCall("read_file", "some output")];
        TurnRecord original = BuildRecord(turnId: "duplicate-id", toolCalls: toolCalls);
        await _store.AppendAsync(original);

        // A second append with the same turn_id violates the PRIMARY KEY constraint on
        // the turns table. The transaction should roll back before writing tool_outputs.
        TurnRecord duplicate = BuildRecord(turnId: "duplicate-id", toolCalls: toolCalls);
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => _store.AppendAsync(duplicate));

        // Only the first turn's tool_outputs row should exist.
        IReadOnlyList<ToolCallRecord> outputs = await _store.SearchToolOutputsAsync("some output");
        Assert.Single(outputs);
    }

    // ── GetTurnsAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTurnsAsync_ReturnsAllTurnsForSession_InInsertionOrder()
    {
        TurnRecord first = BuildRecord(sessionId: "session-a", turnId: "turn-01");
        TurnRecord second = BuildRecord(sessionId: "session-a", turnId: "turn-02");
        TurnRecord third = BuildRecord(sessionId: "session-a", turnId: "turn-03");

        await _store.AppendAsync(first);
        await _store.AppendAsync(second);
        await _store.AppendAsync(third);

        IReadOnlyList<TurnRecord> turns = await _store.GetTurnsAsync("session-a");

        Assert.Equal(3, turns.Count);
        Assert.Equal("turn-01", turns[0].TurnId);
        Assert.Equal("turn-02", turns[1].TurnId);
        Assert.Equal("turn-03", turns[2].TurnId);
    }

    [Fact]
    public async Task GetTurnsAsync_ExcludesTurnsFromOtherSessions()
    {
        TurnRecord sessionA = BuildRecord(sessionId: "session-a", turnId: "turn-a1");
        TurnRecord sessionB = BuildRecord(sessionId: "session-b", turnId: "turn-b1");

        await _store.AppendAsync(sessionA);
        await _store.AppendAsync(sessionB);

        IReadOnlyList<TurnRecord> results = await _store.GetTurnsAsync("session-a");

        Assert.Single(results);
        Assert.Equal("session-a", results[0].SessionId);
    }

    // ── SearchToolOutputsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task SearchToolOutputsAsync_ReturnsMatchingRows()
    {
        IReadOnlyList<ToolCallRecord> toolCalls =
        [
            BuildToolCall("read_file", "namespace Phelix.Core.Agent;")
        ];

        await _store.AppendAsync(BuildRecord(toolCalls: toolCalls));

        IReadOnlyList<ToolCallRecord> results = await _store.SearchToolOutputsAsync("Phelix");

        Assert.Single(results);
        Assert.Equal("read_file", results[0].Name);
        Assert.Contains("Phelix", results[0].Result);
    }

    [Fact]
    public async Task SearchToolOutputsAsync_ReturnsAtMostMaxResults()
    {
        for (int i = 0; i < 7; i++)
        {
            IReadOnlyList<ToolCallRecord> toolCalls =
            [
                BuildToolCall("read_file", $"matching content number {i}")
            ];
            await _store.AppendAsync(BuildRecord(turnId: $"turn-{i}", toolCalls: toolCalls));
        }

        IReadOnlyList<ToolCallRecord> results = await _store.SearchToolOutputsAsync("matching content", maxResults: 3);

        Assert.True(results.Count <= 3);
    }

    [Fact]
    public async Task SearchToolOutputsAsync_ReturnsEmpty_WhenNoMatch()
    {
        IReadOnlyList<ToolCallRecord> toolCalls =
        [
            BuildToolCall("read_file", "completely unrelated output")
        ];
        await _store.AppendAsync(BuildRecord(toolCalls: toolCalls));

        IReadOnlyList<ToolCallRecord> results = await _store.SearchToolOutputsAsync("xyzzy_no_match");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchToolOutputsAsync_DoesNotThrow_WhenQueryContainsFtsSpecialCharacters()
    {
        IReadOnlyList<ToolCallRecord> toolCalls =
        [
            BuildToolCall("read_file", "namespace Phelix.Core.Agent;")
        ];
        await _store.AppendAsync(BuildRecord(toolCalls: toolCalls));

        // Queries like "AGENTS.md" or "(read_file)" contain characters that are
        // syntactically significant to FTS5 and produced a parse error before sanitization.
        IReadOnlyList<ToolCallRecord> dotQuery = await _store.SearchToolOutputsAsync("AGENTS.md");
        IReadOnlyList<ToolCallRecord> parenQuery = await _store.SearchToolOutputsAsync("(read_file)");
        IReadOnlyList<ToolCallRecord> dashQuery = await _store.SearchToolOutputsAsync("some-thing");

        // The assertion is that none of these threw — the results themselves are irrelevant.
        Assert.NotNull(dotQuery);
        Assert.NotNull(parenQuery);
        Assert.NotNull(dashQuery);
    }
}
