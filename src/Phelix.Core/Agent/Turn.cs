using Microsoft.Extensions.AI;
using Phelix.Core.Session;

namespace Phelix.Core.Agent;

public record Turn(
    IReadOnlyList<ChatMessage> Messages,
    ChatResponse Response,
    DateTimeOffset Timestamp,
    UsageSummary Usage,
    IReadOnlyList<ToolCallRecord> ToolCalls,
    TurnExitReason ExitReason = TurnExitReason.Completed
);
