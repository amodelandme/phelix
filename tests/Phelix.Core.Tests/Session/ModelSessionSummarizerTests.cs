using Microsoft.Extensions.AI;
using Phelix.Core.Agent;
using Phelix.Core.Session;

namespace Phelix.Core.Tests.Session;

public class ModelSessionSummarizerTests : IDisposable
{
    readonly SqliteSessionStore _store;
    readonly ModelSessionSummarizer _summarizer;
    readonly FakeSummarizingChatClient _fakeClient;

    public ModelSessionSummarizerTests()
    {
        _store = new SqliteSessionStore(":memory:");
        _fakeClient = new FakeSummarizingChatClient("This session: the model fixed a bug.");
        _summarizer = new ModelSessionSummarizer(_fakeClient, _store);
    }

    public void Dispose() => _store.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    static TurnRecord BuildRecord(
        string sessionId,
        string turnId,
        string userMessage = "do something",
        string assistantMessage = "done",
        IReadOnlyList<ToolCallRecord>? toolCalls = null)
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
            ToolCalls: toolCalls ?? [],
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

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SummarizeAsync_ReturnsModelResponse_AsString()
    {
        await _store.AppendAsync(BuildRecord("session-a", "turn-01"));

        string summary = await _summarizer.SummarizeAsync("session-a");

        Assert.Equal("This session: the model fixed a bug.", summary);
    }

    [Fact]
    public async Task SummarizeAsync_BuildsTranscriptFromAllTurns()
    {
        await _store.AppendAsync(BuildRecord("session-a", "turn-01", userMessage: "first question"));
        await _store.AppendAsync(BuildRecord("session-a", "turn-02", userMessage: "second question"));

        await _summarizer.SummarizeAsync("session-a");

        // The fake client captures the prompt so we can verify both turns appeared in it.
        Assert.Contains("first question", _fakeClient.LastPrompt);
        Assert.Contains("second question", _fakeClient.LastPrompt);
    }

    [Fact]
    public async Task SummarizeAsync_ReturnsEmptyString_OnEmptyModelResponse()
    {
        FakeSummarizingChatClient emptyResponseClient = new(responseText: null);
        ModelSessionSummarizer summarizer = new(emptyResponseClient, _store);

        await _store.AppendAsync(BuildRecord("session-a", "turn-01"));

        string summary = await summarizer.SummarizeAsync("session-a");

        Assert.Equal(string.Empty, summary);
    }
}

/// <summary>
/// Returns a canned <paramref name="responseText"/> from <see cref="GetResponseAsync"/>.
/// Captures the last prompt sent so tests can assert on transcript content.
/// </summary>
sealed class FakeSummarizingChatClient(string? responseText) : IChatClient
{
    public string LastPrompt { get; private set; } = string.Empty;

    public ChatClientMetadata Metadata => new("fake", null, null);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastPrompt = string.Concat(messages.Select(m => m.Text ?? string.Empty));

        ChatMessage responseMessage = new(ChatRole.Assistant, responseText ?? string.Empty);
        ChatResponse response = new([responseMessage]);

        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
