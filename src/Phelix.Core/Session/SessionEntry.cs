using Microsoft.Extensions.AI;
using Phelix.Core.Agent;

namespace Phelix.Core.Session;

/// <summary>
/// A serializable snapshot of one completed turn, written to the session log.
/// </summary>
/// <remarks>
/// Owns only what is needed for replay and review: the user prompt, the assistant
/// response text, the model that produced it, and when it happened. Full
/// <see cref="ChatMessage"/> history is owned by <see cref="Turn"/> — this record
/// carries only the fields that survive serialization cleanly.
/// </remarks>
/// <param name="UserMessage">The raw user prompt for this turn.</param>
/// <param name="AssistantMessage">The assembled assistant response text.</param>
/// <param name="ModelId">The model identifier reported by the response.</param>
/// <param name="Timestamp">UTC instant the turn completed.</param>
public record SessionEntry(
    string UserMessage,
    string AssistantMessage,
    string ModelId,
    DateTimeOffset Timestamp
)
{
    /// <summary>
    /// Constructs a <see cref="SessionEntry"/> from a completed <see cref="Turn"/> and the
    /// original user prompt.
    /// </summary>
    /// <param name="turn">The turn returned by <see cref="AgentLoop.RunTurnAsync"/>.</param>
    /// <param name="userMessage">The user prompt passed into that turn.</param>
    public static SessionEntry FromTurn(Turn turn, string userMessage)
    {
        string assistantText = turn.Response.Text ?? string.Empty;
        string modelId = turn.Response.ModelId ?? string.Empty;
        return new SessionEntry(userMessage, assistantText, modelId, turn.Timestamp);
    }
}
