using Microsoft.Extensions.AI;

namespace Phelix.Core.Agent;

/// <summary>
/// An immutable record of a single completed interaction with the model.
/// </summary>
/// <remarks>
/// Returned by <see cref="AgentLoop.RunTurnAsync"/> after every call, regardless
/// of whether streaming was used. Serves as the unit of session history — callers
/// append <see cref="Messages"/> to their conversation history and pass it back
/// on the next turn.
/// </remarks>
/// <param name="Messages">
/// The full message list sent to the model this turn, including prior history
/// and the new user message. Ready to be passed as history on the next call.
/// </param>
/// <param name="Response">
/// The raw response returned by the <see cref="IChatClient"/>. Contains the
/// assistant message, token usage, and finish reason.
/// </param>
/// <param name="Timestamp">The UTC time at which the model response was received.</param>
/// <param name="ExitReason">Why the turn stopped — natural completion or turn limit.</param>
public record Turn(
    IReadOnlyList<ChatMessage> Messages,
    ChatResponse Response,
    DateTimeOffset Timestamp,
    TurnExitReason ExitReason = TurnExitReason.Completed
);
