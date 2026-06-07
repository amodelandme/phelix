namespace Phelix.Core.Config;

public record ModelConfig
{
    public required string Provider { get; init; }
    public required string ModelId { get; init; }
    public int MaxTurns { get; init; } = 5;
}
