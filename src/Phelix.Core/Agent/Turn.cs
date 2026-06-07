using Microsoft.Extensions.AI;
using Phelix.Core.Session;

namespace Phelix.Core.Agent;

/// <summary>
/// The runtime artifact produced by a single agent loop turn.
/// </summary>
/// <remarks>
/// <c>Turn</c> is the live, in-memory result of one call to <c>AgentLoop.RunTurnAsync</c>.
/// It is never persisted directly — <see cref="TurnRecord"/> is the durable log shape derived
/// from it via <c>TurnRecord.FromTurn</c>. The two types have different shapes by design:
/// <c>Turn</c> carries everything needed for the next loop iteration; <c>TurnRecord</c> stores
/// only what is useful for observability.
/// </remarks>
public record Turn(
    /// <summary>
    /// The complete message exchange for this turn, including raw tool-call and tool-result
    /// messages. Consumed exclusively by <see cref="SessionLogger"/>; never passed back to
    /// the model.
    /// </summary>
    IReadOnlyList<ChatMessage> Messages,

    /// <summary>
    /// The pruned message list passed as <c>conversationHistory</c> on the next
    /// <c>RunTurnAsync</c> call. Tool exchange messages are stripped because the model
    /// already synthesized them into the final reply — re-sending them wastes context.
    /// </summary>
    IReadOnlyList<ChatMessage> ContextMessages,

    /// <summary>
    /// The model's final text response for this turn. This is the last assistant message
    /// after all tool-call rounds complete, not an intermediate tool-call response.
    /// </summary>
    ChatResponse Response,

    /// <summary>
    /// UTC timestamp set at the moment the agent loop exits for this turn.
    /// </summary>
    DateTimeOffset Timestamp,

    /// <summary>
    /// Aggregate token counts across all inner model calls in this turn — tool-call
    /// rounds and the final response combined.
    /// </summary>
    UsageSummary Usage,

    /// <summary>
    /// Ordered list of every tool invocation that occurred during this turn. Each entry
    /// holds the truncated result string the model actually received, capped at
    /// <c>AgentLoop.MaxToolOutputChars</c>.
    /// </summary>
    IReadOnlyList<ToolCallRecord> ToolCalls,

    /// <summary>
    /// The reason the agent loop stopped. <see cref="TurnExitReason.Completed"/> is the
    /// normal exit path. <see cref="TurnExitReason.TurnLimitReached"/> means
    /// <c>MaxTurns</c> was hit mid-tool-chain and the loop halted without a final reply.
    /// </summary>
    TurnExitReason ExitReason = TurnExitReason.Completed
);
