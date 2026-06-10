using Microsoft.Extensions.AI;

namespace Phelix.Core.Session;

/// <summary>
/// Decides whether the conversation history should be compacted before the next turn.
/// </summary>
/// <remarks>
/// Implementations must be pure — no side effects, no I/O. The decision is
/// synchronous because it reads only what is already in memory. Returning
/// <see langword="true"/> means the caller must compact; the policy itself
/// never performs the compaction.
/// </remarks>
public interface ICompactionPolicy
{
    /// <summary>
    /// Returns <see langword="true"/> when the caller should compact
    /// <paramref name="history"/> before sending it to the model again.
    /// </summary>
    /// <param name="history">The current conversation message list.</param>
    bool ShouldCompact(IReadOnlyList<ChatMessage> history);
}
