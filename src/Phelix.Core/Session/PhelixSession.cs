using Microsoft.Extensions.AI;
using Phelix.Core.Agent;

namespace Phelix.Core.Session;

/// <summary>
/// Owns one conversation's lifetime: history, compaction, and turn logging.
/// </summary>
/// <remarks>
/// <para>
/// Extracts the session orchestration that previously lived in <c>Phelix.Cli/Program.cs</c>
/// so that both <c>Phelix.Cli</c> and <c>Phelix.Tui</c> can drive the same logic without
/// duplication.
/// </para>
/// <para>
/// <see cref="RunTurnAsync"/> always returns a <see cref="TurnResult"/> — it never throws.
/// On failure the error is logged with <see cref="TurnExitReason.Error"/> and
/// <see cref="ConversationHistory"/> is left unchanged, so the session remains in a
/// consistent state regardless of whether the turn succeeded.
/// </para>
/// </remarks>
/// <param name="agentLoop">The configured agent loop to invoke each turn.</param>
/// <param name="sessionStore">Durable storage for completed turns.</param>
/// <param name="compactionPolicy">Decides when history is too long to send to the model.</param>
/// <param name="summarizer">Produces a summary used to replace history after compaction.</param>
public sealed class PhelixSession(
    AgentLoop agentLoop,
    ISessionStore sessionStore,
    ICompactionPolicy compactionPolicy,
    ISessionSummarizer summarizer)
{
    /// <summary>
    /// The current conversation history. Updated after each successful turn and after
    /// compaction. Unchanged when a turn fails.
    /// </summary>
    public IReadOnlyList<ChatMessage> ConversationHistory { get; private set; } = [];

    /// <summary>
    /// The total number of input and output tokens consumed across all turns this session.
    /// </summary>
    public int TotalTokenCount { get; private set; }

    /// <summary>
    /// Appends <paramref name="userMessage"/> to history, runs the agent loop, logs the
    /// result, and checks the compaction policy.
    /// </summary>
    /// <remarks>
    /// On success, <see cref="ConversationHistory"/> and <see cref="TotalTokenCount"/>
    /// are updated. On failure, both are left unchanged; the failed turn is logged with
    /// <see cref="TurnExitReason.Error"/> so the session log remains complete.
    /// </remarks>
    /// <param name="userMessage">The user's input for this turn.</param>
    /// <param name="callbacks">
    /// Optional per-turn callbacks for streaming output and tool-call events.
    /// Passed through to <see cref="AgentLoop.RunTurnAsync"/> unchanged.
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation to the model and tool calls.</param>
    /// <returns>
    /// <see cref="TurnResult.Success"/> on a normal exit or a turn-limit halt.
    /// <see cref="TurnResult.Failure"/> when an exception is caught.
    /// </returns>
    public async Task<TurnResult> RunTurnAsync(
        string userMessage,
        TurnCallbacks callbacks = default,
        CancellationToken cancellationToken = default)
    {
        string turnId = Guid.NewGuid().ToString("N");
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;

        try
        {
            Turn completedTurn = await agentLoop.RunTurnAsync(
                ConversationHistory,
                userMessage,
                callbacks,
                cancellationToken);

            TurnRecord record = TurnRecord.FromTurn(
                completedTurn,
                sessionId: SessionLogger.SessionId,
                userMessage: userMessage,
                turnId: turnId,
                startedAt: startedAt);

            await SessionLogger.AppendAsync(record, cancellationToken: cancellationToken);
            await sessionStore.AppendAsync(record, cancellationToken);

            ConversationHistory = completedTurn.ContextMessages;
            TotalTokenCount += completedTurn.Usage.InputTokens + completedTurn.Usage.OutputTokens;

            await TryCompactAsync(cancellationToken);

            return new TurnResult.Success(completedTurn);
        }
        catch (Exception ex)
        {
            await LogFailedTurnAsync(turnId, startedAt, userMessage, cancellationToken);
            return new TurnResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Compacts history when the policy threshold is crossed, replacing it with a
    /// model-generated summary injected as a system message.
    /// </summary>
    /// <remarks>
    /// A failed or empty summary is silently ignored — the existing history is kept.
    /// Compaction failure is non-fatal; the session continues with uncompacted history.
    /// </remarks>
    async Task TryCompactAsync(CancellationToken cancellationToken)
    {
        if (!compactionPolicy.ShouldCompact(ConversationHistory))
            return;

        string summary = await summarizer.SummarizeAsync(SessionLogger.SessionId, cancellationToken);

        if (string.IsNullOrEmpty(summary))
            return;

        ConversationHistory =
        [
            new ChatMessage(ChatRole.System, $"[Session compacted]\n\n{summary}")
        ];
    }

    /// <summary>
    /// Writes a minimal <see cref="TurnRecord"/> for a failed turn to both log sinks.
    /// Best-effort — logging errors are swallowed so the failure path stays clean.
    /// </summary>
    async Task LogFailedTurnAsync(
        string turnId,
        DateTimeOffset startedAt,
        string userMessage,
        CancellationToken cancellationToken)
    {
        TurnRecord failedRecord = new(
            TurnId: turnId,
            SessionId: SessionLogger.SessionId,
            UserMessage: userMessage,
            FinalAssistantMessage: string.Empty,
            ModelId: string.Empty,
            StartedAt: startedAt,
            CompletedAt: DateTimeOffset.UtcNow,
            ExitReason: TurnExitReason.Error,
            Usage: new UsageSummary(0, 0),
            ToolCalls: []
        );

        try
        {
            await SessionLogger.AppendAsync(failedRecord, cancellationToken: cancellationToken);
            await sessionStore.AppendAsync(failedRecord, cancellationToken);
        }
        catch
        {
            // Logging failure must not mask the original exception.
        }
    }
}
