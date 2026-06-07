namespace Phelix.Core.Config;

public record PhelixConfig
{
    public static readonly PhelixConfig Default = new()
    {
        ActiveModel = "qwen-flash",
        SystemPrompt = "You are a helpful coding assistant.",
        Providers = new Dictionary<string, ProviderConfig>
        {
            ["openrouter"] = new()
            {
                ApiKeyEnv = "OPENROUTER_API_KEY",
                BaseUrl = "https://openrouter.ai/api/v1"
            }
        },
        Models = new Dictionary<string, ModelConfig>
        {
            ["qwen-flash"] = new()
            {
                Provider = "openrouter",
                ModelId = "qwen/qwen3.5-flash-02-23",
                MaxTurns = 5
            }
        }
    };

    public required string ActiveModel { get; init; }
    public required string SystemPrompt { get; init; }
    public required IReadOnlyDictionary<string, ProviderConfig> Providers { get; init; }
    public required IReadOnlyDictionary<string, ModelConfig> Models { get; init; }
}
