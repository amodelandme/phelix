namespace Phelix.Core.Agent;

/// <summary>
/// Configuration for a single agent session.
/// </summary>
/// <remarks>
/// Passed to <see cref="AgentLoop"/> at construction time and treated as immutable
/// for the lifetime of the loop. All required properties must be set at the call
/// site — there are no safe defaults for model identity or behavior.
/// </remarks>
public record AgentOptions
{
    const int MaxTurnsDefault = 5;
    const int CompactionThresholdTokensDefault = 40_000;

    /// <summary>
    /// The model identifier forwarded to the <see cref="Microsoft.Extensions.AI.IChatClient"/>.
    /// Format is provider-specific (e.g. "claude-sonnet-4-6", "mistralai/mistral-7b-instruct:free").
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// The system prompt injected at the start of every conversation.
    /// Defines the agent's role and behavioral constraints.
    /// </summary>
    public required string SystemPrompt { get; init; }

    /// <summary>
    /// Maximum number of turns the loop will execute before halting.
    /// Guards against runaway agentic sessions. Defaults to <c>5</c>.
    /// </summary>
    public int MaxTurns { get; init; } = MaxTurnsDefault;

    /// <summary>
    /// Estimated token count at which the REPL loop compacts conversation history.
    /// Defaults to <c>40,000</c> — roughly half of a typical 80K context window,
    /// leaving headroom for the heuristic's imprecision before the hard limit is reached.
    /// </summary>
    public int CompactionThresholdTokens { get; init; } = CompactionThresholdTokensDefault;

    /// <summary>
    /// The gate consulted before each tool call to decide whether the call may proceed.
    /// Defaults to <see cref="AutoApproveGate"/> when <c>null</c> — matching the previous
    /// behaviour where all tools executed without interruption.
    /// </summary>
    /// <remarks>
    /// Supply an <see cref="InteractiveApprovalGate"/> for interactive sessions or a
    /// custom implementation in tests. The gate is set once at startup and is constant
    /// for the lifetime of the session.
    /// </remarks>
    public IApprovalGate ApprovalGate { get; init; } = new AutoApproveGate();
}
