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
}
