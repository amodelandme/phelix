using Phelix.Core.Agent;

namespace Phelix.Core.Session;

public record TurnRecord(
    string TurnId,
    string SessionId,
    string UserMessage,
    string FinalAssistantMessage,
    string ModelId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    TurnExitReason ExitReason,
    UsageSummary Usage,
    IReadOnlyList<ToolCallRecord> ToolCalls
)
{
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
