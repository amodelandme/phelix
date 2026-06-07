namespace Phelix.Core.Config;

/// <summary>
/// Configuration for a single API provider entry in <see cref="PhelixConfig.Providers"/>.
/// </summary>
/// <remarks>
/// The API key value is never stored in config. Only the environment variable name is stored;
/// <c>PhelixHost</c> reads the actual key from the environment at startup.
/// </remarks>
public record ProviderConfig
{
    /// <summary>
    /// The name of the environment variable that holds the API key for this provider
    /// (e.g. <c>"OPENROUTER_API_KEY"</c>). The key value itself is never stored in config.
    /// </summary>
    public required string ApiKeyEnv { get; init; }

    /// <summary>
    /// The OpenAI-compatible base URL for this provider's API
    /// (e.g. <c>"https://openrouter.ai/api/v1"</c>). Passed to <c>OpenAIClientOptions</c>
    /// when constructing the <c>IChatClient</c>.
    /// </summary>
    public required string BaseUrl { get; init; }
}
