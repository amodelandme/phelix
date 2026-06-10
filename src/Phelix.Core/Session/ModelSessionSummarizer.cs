using System.Text;
using Microsoft.Extensions.AI;

namespace Phelix.Core.Session;

/// <summary>
/// <see cref="ISessionSummarizer"/> that calls the model to produce a session summary.
/// </summary>
/// <remarks>
/// Reads all turns for the session from <see cref="ISessionStore"/>, builds a compact
/// transcript, and sends it to <paramref name="chatClient"/> with a fixed summarizer
/// prompt. The model is asked to produce a concise summary (under 400 tokens) covering
/// the user's goal, key decisions, files touched, and remaining work.
/// <para>
/// Tool call results are intentionally omitted from the transcript. Only the tool name,
/// arguments, and status are included — this keeps the summarizer prompt well under the
/// context limit even for long sessions. Full results remain queryable via the
/// <c>search_session</c> tool.
/// </para>
/// <para>
/// The <paramref name="chatClient"/> is injected so tests can pass a fake without
/// triggering real model calls.
/// </para>
/// </remarks>
/// <param name="chatClient">The model client used to generate the summary.</param>
/// <param name="store">Session store from which turn history is read.</param>
public sealed class ModelSessionSummarizer(IChatClient chatClient, ISessionStore store) : ISessionSummarizer
{
    const string SummarizerPrompt = """
        You are summarizing a coding agent session for context compaction.
        Produce a concise summary (under 400 tokens) covering:
        - The user's original goal
        - Key decisions made
        - Files read or written, with the action taken on each
        - The current state of the work and what remains

        Output plain prose. No headers. No lists. Start with "This session:".
        """;

    /// <inheritdoc/>
    public async Task<string> SummarizeAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TurnRecord> turns = await store.GetTurnsAsync(sessionId, cancellationToken);
        string transcript = BuildTranscript(turns);

        IReadOnlyList<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.User, $"{SummarizerPrompt}\n\n---\n\n{transcript}")
        ];

        ChatResponse response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

        return response.Text ?? string.Empty;
    }

    static string BuildTranscript(IReadOnlyList<TurnRecord> turns)
    {
        StringBuilder transcript = new();

        foreach (TurnRecord turn in turns)
        {
            transcript.AppendLine($"User: {turn.UserMessage}");
            transcript.AppendLine($"Assistant: {turn.FinalAssistantMessage}");

            foreach (ToolCallRecord toolCall in turn.ToolCalls)
                transcript.AppendLine($"  [{toolCall.Name}({toolCall.ArgumentsJson}) → {toolCall.Status}]");

            transcript.AppendLine();
        }

        return transcript.ToString();
    }
}
