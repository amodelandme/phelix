using Microsoft.Extensions.AI;
using Phelix.Core.Agent;
using Phelix.Core.Tools;

namespace Phelix.Core.Tests.Agent;

public class AgentLoopTests
{
    // --- TruncateToolOutput ---

    [Fact]
    public void TruncateToolOutput_ShortResult_ReturnsUnchanged()
    {
        string result = new('a', 100);

        string truncated = AgentLoop.TruncateToolOutput(result, 2000);

        Assert.Equal(result, truncated);
    }

    [Fact]
    public void TruncateToolOutput_ResultExactlyAtLimit_ReturnsUnchanged()
    {
        string result = new('a', 2000);

        string truncated = AgentLoop.TruncateToolOutput(result, 2000);

        Assert.Equal(result, truncated);
    }

    [Fact]
    public void TruncateToolOutput_LongResult_ReturnsHeadAndTail()
    {
        string head = new('H', 1600);
        string middle = new('M', 500);
        string tail = new('T', 400);
        string result = head + middle + tail;

        string truncated = AgentLoop.TruncateToolOutput(result, 2000);

        Assert.StartsWith(head, truncated);
        Assert.EndsWith(tail, truncated);
    }

    [Fact]
    public void TruncateToolOutput_LongResult_NoticeContainsTruncatedCharCount()
    {
        string result = new('a', 3000);

        string truncated = AgentLoop.TruncateToolOutput(result, 2000);

        // 3000 total - 1600 head - 400 tail = 1000 truncated
        Assert.Contains("1000 chars truncated", truncated);
    }

    [Fact]
    public void TruncateToolOutput_MaxCharsZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AgentLoop.TruncateToolOutput("some result", 0));
    }

    // --- BuildContextMessages (via RunTurnAsync) ---

    [Fact]
    public async Task RunTurnAsync_ContextMessages_ExcludesToolRoleMessages()
    {
        FakeChatClient fakeClient = new FakeChatClient(toolCallName: "list_files", toolCallResult: "src/Foo.cs");
        AgentOptions options = new() { ModelId = "fake", SystemPrompt = string.Empty, MaxTurns = 5 };
        ToolRegistry toolRegistry = FakeToolRegistry.Build("list_files", "src/Foo.cs");
        AgentLoop loop = new(fakeClient, options, toolRegistry);

        Turn turn = await loop.RunTurnAsync([], "list files", cancellationToken: CancellationToken.None);

        Assert.DoesNotContain(turn.ContextMessages, m => m.Role == ChatRole.Tool);
    }

    [Fact]
    public async Task RunTurnAsync_ContextMessages_ExcludesAssistantToolCallMessages()
    {
        FakeChatClient fakeClient = new FakeChatClient(toolCallName: "list_files", toolCallResult: "src/Foo.cs");
        AgentOptions options = new() { ModelId = "fake", SystemPrompt = string.Empty, MaxTurns = 5 };
        ToolRegistry toolRegistry = FakeToolRegistry.Build("list_files", "src/Foo.cs");
        AgentLoop loop = new(fakeClient, options, toolRegistry);

        Turn turn = await loop.RunTurnAsync([], "list files", cancellationToken: CancellationToken.None);

        Assert.DoesNotContain(turn.ContextMessages, m =>
            m.Role == ChatRole.Assistant &&
            m.Contents.Count > 0 &&
            m.Contents.All(c => c is FunctionCallContent));
    }

    [Fact]
    public async Task RunTurnAsync_ContextMessages_RetainsUserAndFinalAssistantMessages()
    {
        FakeChatClient fakeClient = new FakeChatClient(toolCallName: "list_files", toolCallResult: "src/Foo.cs");
        AgentOptions options = new() { ModelId = "fake", SystemPrompt = string.Empty, MaxTurns = 5 };
        ToolRegistry toolRegistry = FakeToolRegistry.Build("list_files", "src/Foo.cs");
        AgentLoop loop = new(fakeClient, options, toolRegistry);

        Turn turn = await loop.RunTurnAsync([], "list files", cancellationToken: CancellationToken.None);

        Assert.Contains(turn.ContextMessages, m => m.Role == ChatRole.User);
        Assert.Contains(turn.ContextMessages, m =>
            m.Role == ChatRole.Assistant &&
            m.Contents.Any(c => c is TextContent));
    }
}
