using Microsoft.Extensions.AI;
using Phelix.Core.Agent;
using Phelix.Core.Session;
using Phelix.Core.Tools;

namespace Phelix.Core.Tests.Tools;

public class SearchSessionToolTests : IDisposable
{
    readonly SqliteSessionStore _store;
    readonly SearchSessionTool _tool;

    public SearchSessionToolTests()
    {
        _store = new SqliteSessionStore(":memory:");
        _tool = new SearchSessionTool(_store);
    }

    public void Dispose() => _store.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    static TurnRecord BuildRecordWithToolCall(string turnId, string toolName, string result)
    {
        ToolCallRecord toolCall = new(
            CallId: Guid.NewGuid().ToString("N"),
            Name: toolName,
            ArgumentsJson: "{\"path\":\"src/Program.cs\"}",
            Result: result,
            Status: ToolCallStatus.Succeeded
        );

        ChatMessage user = new(ChatRole.User, "do something");
        ChatMessage assistant = new(ChatRole.Assistant, "done");
        ChatResponse response = new([assistant]) { ModelId = "fake-model" };
        IReadOnlyList<ChatMessage> messages = [user, assistant];

        Turn turn = new(
            Messages: messages,
            ContextMessages: messages,
            Response: response,
            Timestamp: DateTimeOffset.UtcNow,
            Usage: new UsageSummary(10, 5),
            ToolCalls: [toolCall],
            ExitReason: TurnExitReason.Completed
        );

        return TurnRecord.FromTurn(
            turn,
            sessionId: "session-a",
            userMessage: "do something",
            turnId: turnId,
            startedAt: DateTimeOffset.UtcNow.AddSeconds(-1)
        );
    }

    static IReadOnlyDictionary<string, object?> QueryParams(string query) =>
        new Dictionary<string, object?> { ["query"] = query };

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ReturnsFormattedResults_ForMatchingQuery()
    {
        await _store.AppendAsync(BuildRecordWithToolCall("turn-01", "read_file", "namespace Phelix.Core;"));

        string result = await _tool.ExecuteAsync(QueryParams("Phelix"), CancellationToken.None);

        Assert.Contains("read_file", result);
        Assert.Contains("namespace Phelix.Core;", result);
        Assert.Contains("Found 1 result(s):", result);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNoResultsMessage_WhenQueryMatchesNothing()
    {
        await _store.AppendAsync(BuildRecordWithToolCall("turn-01", "read_file", "completely unrelated content"));

        string result = await _tool.ExecuteAsync(QueryParams("xyzzy_no_match"), CancellationToken.None);

        Assert.Contains("No session history found matching", result);
        Assert.Contains("xyzzy_no_match", result);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsAtMostFiveResults()
    {
        for (int i = 0; i < 7; i++)
            await _store.AppendAsync(BuildRecordWithToolCall($"turn-{i:D2}", "read_file", $"matching output number {i}"));

        string result = await _tool.ExecuteAsync(QueryParams("matching output"), CancellationToken.None);

        // Count "[N]" markers in the output — each result starts with "[1]", "[2]", etc.
        int resultCount = System.Text.RegularExpressions.Regex.Matches(result, @"^\[\d+\]", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
        Assert.True(resultCount <= 5);
    }
}
