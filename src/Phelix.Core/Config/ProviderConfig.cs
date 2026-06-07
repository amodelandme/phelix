namespace Phelix.Core.Config;

public record ProviderConfig
{
    public required string ApiKeyEnv { get; init; }
    public required string BaseUrl { get; init; }
}
