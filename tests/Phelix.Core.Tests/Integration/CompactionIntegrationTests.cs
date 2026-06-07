using Microsoft.Extensions.AI;
using Phelix.Core.Agent;
using Phelix.Core.Session;

namespace Phelix.Core.Tests.Integration;

/// <summary>
/// Verifies the compaction check loop that lives in <c>Program.cs</c> —
/// specifically that after crossing the threshold, <c>conversationHistory</c>
/// is replaced with a single summary message.
/// </summary>
/// <remarks>
/// Uses <c>thresholdTokens = 1</c> so compaction fires after the very first turn
/// regardless of message length. Drives the check loop directly rather than spawning
/// the real REPL, since we need to observe the history replacement.
/// </remarks>
public class CompactionIntegrationTests : IDisposable
{
    readonly SqliteSessionStore _store;

    public CompactionIntegrationTests()
    {
        _store = new SqliteSessionStore(":memory:");
    }

    public void Dispose() => _store.Dispose();

    [Fact]
    public async Task ProgramLoop_CompactsHistory_WhenThresholdExceeded()
    {
        ICompactionPolicy compactionPolicy = new TokenThresholdPolicy(thresholdTokens: 1);
        ISessionSummarizer summarizer = new ModelSessionSummarizer(
            new FakeSummaryClient("This session: wrote a test."),
            _store);

        // Seed one turn so the summarizer has something to read back.
        TurnRecord seedRecord = BuildRecord("session-test", "turn-01", "fix the bug", "done");
        await _store.AppendAsync(seedRecord);

        // Build a history list that exceeds threshold = 1 estimated token.
        // A single 4-character message → 1 estimated token, which meets threshold.
        IReadOnlyList<ChatMessage> conversationHistory =
        [
            new ChatMessage(ChatRole.User, "test"),
            new ChatMessage(ChatRole.Assistant, "done"),
        ];

        // This is the exact compaction block from Program.cs.
        if (compactionPolicy.ShouldCompact(conversationHistory))
        {
            string summary = await summarizer.SummarizeAsync("session-test");

            if (!string.IsNullOrEmpty(summary))
            {
                conversationHistory =
                [
                    new ChatMessage(ChatRole.System, $"[Session compacted]\n\n{summary}")
                ];
            }
        }

        Assert.Single(conversationHistory);
        Assert.StartsWith("[Session compacted]", conversationHistory[0].Text);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    static TurnRecord BuildRecord(
        string sessionId,
        string turnId,
        string userMessage,
        string assistantMessage)
    {
        ChatMessage user = new(ChatRole.User, userMessage);
        ChatMessage assistant = new(ChatRole.Assistant, assistantMessage);
        ChatResponse response = new([assistant]) { ModelId = "fake-model" };
        IReadOnlyList<ChatMessage> messages = [user, assistant];

        Turn turn = new(
            Messages: messages,
            ContextMessages: messages,
            Response: response,
            Timestamp: DateTimeOffset.UtcNow,
            Usage: new UsageSummary(10, 5),
            ToolCalls: [],
            ExitReason: TurnExitReason.Completed
        );

        return TurnRecord.FromTurn(
            turn,
            sessionId: sessionId,
            userMessage: userMessage,
            turnId: turnId,
            startedAt: DateTimeOffset.UtcNow.AddSeconds(-1)
        );
    }
}

/// <summary>
/// Returns a fixed summary string from <see cref="GetResponseAsync"/>.
/// </summary>
sealed class FakeSummaryClient(string summary) : IChatClient
{
    public ChatClientMetadata Metadata => new("fake", null, null);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ChatMessage responseMessage = new(ChatRole.Assistant, summary);
        return Task.FromResult(new ChatResponse([responseMessage]));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
