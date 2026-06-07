using Phelix.Core.Agent;

namespace Phelix.Core.Session;

/// <summary>
/// The durable log schema for a completed turn, written to the JSONL session file.
/// </summary>
/// <remarks>
/// <c>TurnRecord</c> is intentionally a different shape from <see cref="Turn"/>.
/// <see cref="Turn"/> is the live runtime artifact that carries everything the loop needs;
/// <c>TurnRecord</c> stores only what is useful for observability — the full message list
/// is dropped and only the final assistant reply is kept.
/// The only way to construct a <c>TurnRecord</c> is via <see cref="FromTurn"/>.
/// </remarks>
public record TurnRecord(
    /// <summary>
    /// Per-turn UUID that uniquely identifies this record within the session log.
    /// </summary>
    string TurnId,

    /// <summary>
    /// Process-lifetime UUID shared across all turns in the session. Matches the file
    /// name component written by <see cref="SessionLogger"/>.
    /// </summary>
    string SessionId,

    /// <summary>
    /// The user's input message that triggered this turn.
    /// </summary>
    string UserMessage,

    /// <summary>
    /// The model's final text reply for this turn — the last assistant message after all
    /// tool-call rounds completed. Raw tool-call responses are not included.
    /// </summary>
    string FinalAssistantMessage,

    /// <summary>
    /// The model ID reported by the provider for the response that closed this turn.
    /// </summary>
    string ModelId,

    /// <summary>
    /// UTC timestamp recorded when the turn began, before the first model call.
    /// </summary>
    DateTimeOffset StartedAt,

    /// <summary>
    /// UTC timestamp recorded when the agent loop exited. Sourced from
    /// <see cref="Turn.Timestamp"/>, which is set at the moment the loop returns.
    /// </summary>
    DateTimeOffset CompletedAt,

    /// <summary>
    /// The reason the agent loop stopped for this turn.
    /// </summary>
    TurnExitReason ExitReason,

    /// <summary>
    /// Aggregate token counts for this turn across all inner model calls.
    /// </summary>
    UsageSummary Usage,

    /// <summary>
    /// Ordered list of every tool invocation that occurred during this turn, each with
    /// the truncated result the model received.
    /// </summary>
    IReadOnlyList<ToolCallRecord> ToolCalls
)
{
    /// <summary>
    /// Constructs a <see cref="TurnRecord"/> from a completed <see cref="Turn"/>.
    /// </summary>
    /// <param name="turn">The completed runtime turn to record.</param>
    /// <param name="sessionId">The process-lifetime session UUID.</param>
    /// <param name="userMessage">The user input that initiated the turn.</param>
    /// <param name="turnId">A fresh UUID identifying this record in the session log.</param>
    /// <param name="startedAt">UTC timestamp from before the first model call.</param>
    /// <returns>
    /// A <see cref="TurnRecord"/> suitable for serialization to the JSONL session log.
    /// The full message list from <paramref name="turn"/> is dropped; only the final
    /// assistant reply and tool call records are retained.
    /// </returns>
    public static TurnRecord FromTurn(
        Turn turn,
        string sessionId,
        string userMessage,
        string turnId,
        DateTimeOffset startedAt)
    {
        return new TurnRecord(
            TurnId: turnId,
            SessionId: sessionId,
            UserMessage: userMessage,
            FinalAssistantMessage: turn.Response.Text ?? string.Empty,
            ModelId: turn.Response.ModelId ?? string.Empty,
            StartedAt: startedAt,
            CompletedAt: turn.Timestamp,
            ExitReason: turn.ExitReason,
            Usage: new UsageSummary(turn.Usage.InputTokens, turn.Usage.OutputTokens),
            ToolCalls: turn.ToolCalls
        );
    }
}
