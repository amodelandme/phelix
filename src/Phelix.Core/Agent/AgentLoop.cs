using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Phelix.Core.Session;
using Phelix.Core.Telemetry;
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
    /// Maximum number of characters returned by any single tool call before the result is truncated.
    /// </summary>
    /// <remarks>
    /// Keeps individual tool results from consuming a disproportionate share of the context window.
    /// The head (80%) and tail (20%) are preserved; the middle is replaced with a truncation notice.
    /// </remarks>
    const int MaxToolOutputChars = 2000;
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
        using Activity? turn = PhelixTelemetry.Source.StartActivity(PhelixTelemetry.Spans.Turn);
        turn?.SetTag(PhelixTelemetry.Tags.Turn.ModelId, options.ModelId);

        List<ChatMessage> messages = new List<ChatMessage>(conversationHistory)
          {
              new(ChatRole.User, userMessage)
          };

        ChatOptions chatOptions = new ChatOptions
        {
            ModelId = options.ModelId,
            Instructions = options.SystemPrompt,
            Tools = toolRegistry?.ToAITools()
        };

        int toolTurns = 0;
        int totalInputTokens = 0;
        int totalOutputTokens = 0;
        List<ToolCallRecord> toolCallRecords = [];

        try
        {
            while (true)
            {
                List<ChatResponseUpdate> updates = new();

                await foreach (ChatResponseUpdate update in chatClient.GetStreamingResponseAsync(messages, chatOptions, cancellationToken))
                {
                    if (onChunk is not null && !string.IsNullOrEmpty(update.Text))
                        await onChunk(update.Text);

                    updates.Add(update);
                }

                ChatResponse response = updates.ToChatResponse();

                totalInputTokens += (int)(response.Usage?.InputTokenCount ?? 0);
                totalOutputTokens += (int)(response.Usage?.OutputTokenCount ?? 0);

                ChatMessage assistantMessage = response.Messages[^1];

                messages.Add(assistantMessage);

                bool limitReached = toolTurns >= options.MaxTurns && response.FinishReason == ChatFinishReason.ToolCalls;

                if (response.FinishReason != ChatFinishReason.ToolCalls || toolRegistry is null || limitReached)
                {
                    turn?.SetTag(PhelixTelemetry.Tags.Turn.ToolTurns, toolTurns);
                    turn?.SetTag(PhelixTelemetry.Tags.Turn.InputTokens, totalInputTokens);
                    turn?.SetTag(PhelixTelemetry.Tags.Turn.OutputTokens, totalOutputTokens);

                    TurnExitReason exitReason = limitReached ? TurnExitReason.TurnLimitReached : TurnExitReason.Completed;
                    UsageSummary usage = new(totalInputTokens, totalOutputTokens);
                    IReadOnlyList<ChatMessage> contextMessages = BuildContextMessages(messages);
                    return new Turn(messages, contextMessages, response, DateTimeOffset.UtcNow, usage, toolCallRecords, exitReason);
                }

                List<AIContent> toolResults = [];

                foreach (AIContent content in assistantMessage.Contents)
                {
                    if (content is not FunctionCallContent call)
                        continue;

                    string result;
                    ToolCallStatus status;

                    using (Activity? toolCall = PhelixTelemetry.Source.StartActivity(PhelixTelemetry.Spans.ToolCall))
                    {
                        toolCall?.SetTag(PhelixTelemetry.Tags.Tool.Name, call.Name);

                        if (toolRegistry.TryGet(call.Name, out ITool? tool))
                        {
                            IReadOnlyDictionary<string, object?> args = call.Arguments is not null
                                ? new Dictionary<string, object?>(call.Arguments, StringComparer.Ordinal)
                                : [];

                            result = TruncateToolOutput(
                                await tool!.ExecuteAsync(args, cancellationToken),
                                MaxToolOutputChars);
                            status = ToolCallStatus.Succeeded;
                            toolCall?.SetTag(PhelixTelemetry.Tags.Tool.Success, true);
                        }
                        else
                        {
                            result = $"Error: no tool named '{call.Name}' is registered.";
                            status = ToolCallStatus.Failed;
                            toolCall?.SetTag(PhelixTelemetry.Tags.Tool.Success, false);
                            toolCall?.SetTag(PhelixTelemetry.Tags.Tool.Error, result);
                        }
                    }

                    string argumentsJson = call.Arguments is not null
                        ? JsonSerializer.Serialize(call.Arguments)
                        : "{}";

                    toolCallRecords.Add(new ToolCallRecord(
                        CallId: call.CallId ?? string.Empty,
                        Name: call.Name,
                        ArgumentsJson: argumentsJson,
                        Result: result,
                        Status: status
                    ));

                    toolResults.Add(new FunctionResultContent(call.CallId ?? string.Empty, result));
                }

                messages.Add(new ChatMessage(ChatRole.Tool, toolResults));
                toolTurns++;
            }
        }
        catch (Exception ex)
        {
            turn?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Caps <paramref name="result"/> at <paramref name="maxChars"/> using a head/tail split.
    /// </summary>
    /// <remarks>
    /// Preserves the first 80% and last 20% of <paramref name="maxChars"/> characters,
    /// replacing the middle with a notice that states the number of truncated characters.
    /// Both the session log and the model receive the same truncated value.
    /// </remarks>
    /// <param name="result">The raw tool output string.</param>
    /// <param name="maxChars">Maximum character length of the returned string. Must be positive.</param>
    public static string TruncateToolOutput(string result, int maxChars)
    {
        if (maxChars <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxChars), "maxChars must be positive.");

        if (result.Length <= maxChars)
            return result;

        int headLength = (int)(maxChars * 0.8);
        int tailLength = maxChars - headLength;
        int truncatedCharCount = result.Length - headLength - tailLength;

        string head = result[..headLength];
        string tail = result[^tailLength..];

        return $"{head}\n... [{truncatedCharCount} chars truncated] ...\n{tail}";
    }

    /// <summary>
    /// Strips raw tool exchange messages from <paramref name="messages"/> to produce a
    /// compact history safe to pass as context on the next turn.
    /// </summary>
    /// <remarks>
    /// Removes <see cref="ChatRole.Tool"/> messages and assistant messages composed
    /// entirely of <see cref="FunctionCallContent"/> items. The model already synthesized
    /// tool output into its final reply — re-sending the raw exchange wastes tokens.
    /// </remarks>
    static IReadOnlyList<ChatMessage> BuildContextMessages(List<ChatMessage> messages)
    {
        List<ChatMessage> contextMessages = new(messages.Count);

        foreach (ChatMessage message in messages)
        {
            if (message.Role == ChatRole.Tool)
                continue;

            if (message.Role == ChatRole.Assistant &&
                message.Contents.Count > 0 &&
                message.Contents.All(c => c is FunctionCallContent))
                continue;

            contextMessages.Add(message);
        }

        return contextMessages;
    }
}