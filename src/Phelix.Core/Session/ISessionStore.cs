namespace Phelix.Core.Session;

/// <summary>
/// Durable storage for completed turns and their tool call outputs.
/// </summary>
/// <remarks>
/// Implementations must persist data across process restarts. Every write through
/// <see cref="AppendAsync"/> must be atomic — either the full turn lands in storage
/// or nothing does. Callers depend on this guarantee when reconstructing session
/// history after a crash or compaction event.
/// <para>
/// <see cref="GetTurnsAsync"/> is consumed only by <see cref="ISessionSummarizer"/>;
/// the agent loop never reads back from storage.
/// <see cref="SearchToolOutputsAsync"/> backs the <c>search_session</c> tool.
/// </para>
/// </remarks>
public interface ISessionStore
{
    /// <summary>
    /// Persists a completed turn and all of its tool call outputs atomically.
    /// </summary>
    /// <param name="record">The completed turn to store.</param>
    /// <param name="cancellationToken">Propagates cancellation from the caller.</param>
    Task AppendAsync(TurnRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all turns for the given session in insertion order (ascending <c>StartedAt</c>).
    /// </summary>
    /// <param name="sessionId">The session whose turns to retrieve.</param>
    /// <param name="cancellationToken">Propagates cancellation from the caller.</param>
    Task<IReadOnlyList<TurnRecord>> GetTurnsAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Full-text searches stored tool call outputs and returns the top matches.
    /// </summary>
    /// <param name="query">The FTS5 query string.</param>
    /// <param name="maxResults">Maximum number of results to return. Defaults to <c>5</c>.</param>
    /// <param name="cancellationToken">Propagates cancellation from the caller.</param>
    /// <returns>
    /// At most <paramref name="maxResults"/> matching <see cref="ToolCallRecord"/> entries,
    /// ranked by FTS5 relevance. Returns an empty list when nothing matches.
    /// </returns>
    Task<IReadOnlyList<ToolCallRecord>> SearchToolOutputsAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default);
}
