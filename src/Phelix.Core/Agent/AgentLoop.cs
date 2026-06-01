using Microsoft.Extensions.AI;
using Phelix.Core.Tools;

namespace Phelix.Core.Agent;

/// <summary>
/// Executes the agent control loop: build messages → call model → dispatch tools → repeat until done.
/// </summary>
/// <remarks>
/// Stateless by design. The caller owns conversation history and passes it in on every call.
/// This makes individual turns easy to test and replay without reconstructing loop state.
/// </remarks>
/// <param name="chatClient">
/// The model client abstraction. Any <see cref="IChatClient"/> implementation works —
/// Anthropic, OpenAI, OpenRouter, or a local fake for testing.
/// </param>
/// <param name="options">Session configuration. Treated as immutable for the loop's lifetime.</param>
/// <param name="toolRegistry">
/// Optional registry of tools the model may call. When <c>null</c>, tool dispatch is skipped
/// and the loop behaves as a single-shot request/response pair.
/// </param>
public class AgentLoop(IChatClient chatClient, AgentOptions options, ToolRegistry? toolRegistry = null)
{
    /// <summary>
    /// Appends <paramref name="userMessage"/> to <paramref name="conversationHistory"/>,
    /// calls the model, dispatches any tool calls, and returns the completed turn.
    /// </summary>
    /// <remarks>
    /// When the model returns tool calls, the loop executes each tool, feeds the results back
    /// as <c>ChatRole.Tool</c> messages, and calls the model again. This repeats until the model
    /// returns a stop response or <see cref="AgentOptions.MaxTurns"/> is reached.
    /// </remarks>
    /// <param name="conversationHistory">
    /// All messages exchanged so far this session. Pass an empty list on the first turn.
    /// The returned <see cref="Turn.Messages"/> includes this history plus all new messages
    /// from this turn — pass it back as history on the next call.
    /// </param>
    /// <param name="userMessage">The raw user input for this turn.</param>
    /// <param name="onChunk">
    /// Optional streaming callback. Invoked with each text chunk as it arrives on the final
    /// (non-tool-call) model response. Tool-call intermediate responses are not streamed.
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation to the underlying model calls.</param>
    /// <returns>
    /// A <see cref="Turn"/> containing the full message list, the final model response, and a UTC timestamp.
    /// </returns>
    public async Task<Turn> RunTurnAsync(
        IReadOnlyList<ChatMessage> conversationHistory,
        string userMessage,
        Func<string, Task>? onChunk = null,
        CancellationToken cancellationToken = default)
    {
        List<ChatMessage> messages = new List<ChatMessage>(conversationHistory)
        {
            new(ChatRole.User, userMessage)
        };

        ChatOptions chatOptions = new ChatOptions
        {
            ModelId = options.ModelId,
            Tools = toolRegistry?.ToAITools()
        };

        int toolTurns = 0;

        while (true)
        {
            ChatResponse response = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);

            ChatMessage assistantMessage = response.Messages[^1];

            if (response.FinishReason != ChatFinishReason.ToolCalls || toolRegistry is null)
            {
                // Final response — stream it if a callback was provided, then return.
                if (onChunk is not null && assistantMessage.Text is not null)
                    await onChunk(assistantMessage.Text);

                messages.Add(assistantMessage);
                return new Turn(messages, response, DateTimeOffset.UtcNow);
            }

            if (toolTurns >= options.MaxTurns)
            {
                messages.Add(assistantMessage);
                return new Turn(messages, response, DateTimeOffset.UtcNow);
            }

            // Append the assistant message containing the tool call requests.
            messages.Add(assistantMessage);

            // Execute each tool call and collect results.
            List<AIContent> toolResults = new List<AIContent>();

            foreach (AIContent content in assistantMessage.Contents)
            {
                if (content is not FunctionCallContent call)
                    continue;

                string result;

                if (toolRegistry.TryGet(call.Name, out ITool? tool))
                {
                    IReadOnlyDictionary<string, object?> args = call.Arguments is not null
                        ? new Dictionary<string, object?>(call.Arguments, StringComparer.Ordinal)
                        : new Dictionary<string, object?>();

                    result = await tool!.ExecuteAsync(args, cancellationToken);
                }
                else
                {
                    result = $"Error: no tool named '{call.Name}' is registered.";
                }

                toolResults.Add(new FunctionResultContent(call.CallId, result));
            }

            messages.Add(new ChatMessage(ChatRole.Tool, toolResults));
            toolTurns++;
        }
    }
}
