namespace Phelix.Core.Session;

/// <summary>
/// Produces a plain-text summary of a session's turn history for use after compaction.
/// </summary>
/// <remarks>
/// The returned string is plain text, not a <c>ChatMessage</c>. The caller wraps it
/// in the appropriate message type and role. This keeps the summarizer independent of
/// <c>Microsoft.Extensions.AI</c> message types — a future implementation backed by a
/// graph or index does not need that dependency.
/// </remarks>
public interface ISessionSummarizer
{
    /// <summary>
    /// Produces a summary of the session identified by <paramref name="sessionId"/>.
    /// </summary>
    /// <param name="sessionId">The session to summarize.</param>
    /// <param name="cancellationToken">Propagates cancellation from the caller.</param>
    /// <returns>
    /// A plain-text summary string. Returns <see cref="string.Empty"/> on model
    /// failure — the caller must handle the empty case.
    /// </returns>
    Task<string> SummarizeAsync(string sessionId, CancellationToken cancellationToken = default);
}
