namespace Phelix.Core.Config;

/// <summary>
/// Configuration for a single named model entry in <see cref="PhelixConfig.Models"/>.
/// </summary>
/// <remarks>
/// Each model is a logical name (e.g. <c>"qwen-flash"</c>) that maps to a provider and a
/// provider-specific model identifier. Multiple model entries can share the same provider.
/// </remarks>
public record ModelConfig
{
    /// <summary>
    /// Key into <see cref="PhelixConfig.Providers"/> identifying which provider serves
    /// this model. Must match an entry in <c>Providers</c> or config validation throws.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// The model identifier passed verbatim to <c>IChatClient</c>. Format is
    /// provider-specific (e.g. <c>"anthropic/claude-sonnet-4-6"</c> on OpenRouter).
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// Maximum number of tool-call rounds the agent loop allows before halting with
    /// <c>TurnExitReason.TurnLimitReached</c>. Defaults to 5 when not specified in config.
    /// </summary>
    public int MaxTurns { get; init; } = 5;
}
