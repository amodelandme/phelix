using Microsoft.Extensions.AI;
using Phelix.Core.Tools;

namespace Phelix.Core.Tests.Agent;

/// <summary>
/// Returns one tool-call response followed by a final text response.
/// </summary>
/// <remarks>
/// On the first <see cref="GetStreamingResponseAsync"/> call, streams a single
/// <see cref="FunctionCallContent"/> for <paramref name="toolCallName"/>.
/// On the second call, streams a plain text reply so the loop exits cleanly.
/// </remarks>
sealed class FakeChatClient : IChatClient
{
    readonly string _toolCallName;
    int _callCount;

    public FakeChatClient(string toolCallName, string toolCallResult)
    {
        _toolCallName = toolCallName;
    }

    public ChatClientMetadata Metadata => new("fake", null, null);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (_callCount == 0)
        {
            _callCount++;
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new FunctionCallContent("call_fake_01", _toolCallName, new Dictionary<string, object?>())],
                FinishReason = ChatFinishReason.ToolCalls
            };
        }
        else
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent("Done.")],
                FinishReason = ChatFinishReason.Stop
            };
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>
/// Wraps a single registered <see cref="ITool"/> in a <see cref="ToolRegistry"/> for test use.
/// </summary>
static class FakeToolRegistry
{
    public static ToolRegistry Build(string toolName, string result)
    {
        ToolRegistry registry = new();
        registry.Register(new FakeTool(toolName, result));
        return registry;
    }
}

/// <summary>
/// Returns a fixed <paramref name="result"/> string for any invocation.
/// </summary>
sealed class FakeTool : ITool
{
    readonly string _result;

    public FakeTool(string name, string result)
    {
        Name = name;
        _result = result;
    }

    public string Name { get; }
    public string Description => "Fake tool for testing.";

    public Task<string> ExecuteAsync(
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        Task.FromResult(_result);

    public AITool ToAITool() =>
        AIFunctionFactory.Create(
            (CancellationToken ct) => ExecuteAsync(new Dictionary<string, object?>(), ct),
            Name,
            Description);
}
