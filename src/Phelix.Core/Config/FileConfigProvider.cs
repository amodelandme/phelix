using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Phelix.Core.Config;

/// <summary>
/// Production <see cref="IConfigProvider"/> that reads <c>~/.phelix/config.yaml</c>.
/// </summary>
/// <remarks>
/// Deserializes the YAML file into private <c>Raw*</c> intermediary types, then maps them
/// to the domain records via <see cref="Map"/>. The <c>Raw*</c> nested classes are private
/// deserialization targets for YamlDotNet — they are not part of the public contract and
/// must not be used outside this file. All name resolution and required-field validation
/// happens inside <see cref="Map"/>; partial state is never returned.
/// </remarks>
public class FileConfigProvider(string filePath) : IConfigProvider
{
    static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Reads and parses the config file at the path supplied to the constructor.
    /// </summary>
    /// <returns>A fully mapped <see cref="PhelixConfig"/>.</returns>
    /// <exception cref="ConfigException">
    /// Thrown when the file cannot be read, cannot be parsed as YAML, or contains
    /// missing required fields (detected during <see cref="Map"/>).
    /// </exception>
    public PhelixConfig Load()
    {
        string yaml;

        try
        {
            yaml = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            throw new ConfigException($"Could not read config file '{filePath}': {ex.Message}");
        }

        RawConfig raw;

        try
        {
            raw = Deserializer.Deserialize<RawConfig>(yaml);
        }
        catch (Exception ex)
        {
            throw new ConfigException($"Could not parse config file '{filePath}': {ex.Message}");
        }

        return Map(raw);
    }

    /// <summary>
    /// Maps raw deserialized YAML types to domain config records, throwing
    /// <see cref="ConfigException"/> for any missing required field.
    /// </summary>
    static PhelixConfig Map(RawConfig raw)
    {
        Dictionary<string, ProviderConfig> providers = raw.Providers
            .ToDictionary(
                kv => kv.Key,
                kv => new ProviderConfig
                {
                    ApiKeyEnv = kv.Value.ApiKeyEnv ?? throw new ConfigException($"Provider '{kv.Key}' is missing 'api_key_env'."),
                    BaseUrl = kv.Value.BaseUrl ?? throw new ConfigException($"Provider '{kv.Key}' is missing 'base_url'.")
                },
                StringComparer.Ordinal);

        Dictionary<string, ModelConfig> models = raw.Models
            .ToDictionary(
                kv => kv.Key,
                kv => new ModelConfig
                {
                    Provider = kv.Value.Provider ?? throw new ConfigException($"Model '{kv.Key}' is missing 'provider'."),
                    ModelId = kv.Value.ModelId ?? throw new ConfigException($"Model '{kv.Key}' is missing 'model_id'."),
                    MaxTurns = kv.Value.MaxTurns ?? ModelConfig.DefaultMaxTurns,
                    Retry = MapRetryPolicy(kv.Value.Retry)
                },
                StringComparer.Ordinal);

        string activeModel = raw.ActiveModel
            ?? (models.Count > 0 ? models.Keys.First() : throw new ConfigException("No models defined in config."));

        string systemPrompt = raw.SystemPrompt
            ?? PhelixConfig.Default.SystemPrompt;

        return new PhelixConfig
        {
            ActiveModel = activeModel,
            SystemPrompt = systemPrompt,
            Providers = providers,
            Models = models,
            Retry = MapRetryPolicy(raw.Retry)
        };
    }

    static RetryPolicy? MapRetryPolicy(RawRetryPolicy? raw)
    {
        if (raw is null)
            return null;

        RetryPolicy defaults = RetryPolicy.Default;

        return new RetryPolicy
        {
            MaxRetries = raw.MaxRetries ?? defaults.MaxRetries,
            BaseDelay = raw.BaseDelaySeconds.HasValue
                ? TimeSpan.FromSeconds(raw.BaseDelaySeconds.Value)
                : defaults.BaseDelay,
            MaxDelay = raw.MaxDelaySeconds.HasValue
                ? TimeSpan.FromSeconds(raw.MaxDelaySeconds.Value)
                : defaults.MaxDelay
        };
    }

    // YamlDotNet target — nullable fields because any key may be absent
    class RawConfig
    {
        public string? ActiveModel { get; set; }
        public string? SystemPrompt { get; set; }
        public Dictionary<string, RawProviderConfig> Providers { get; set; } = [];
        public Dictionary<string, RawModelConfig> Models { get; set; } = [];
        public RawRetryPolicy? Retry { get; set; }
    }

    class RawProviderConfig
    {
        public string? ApiKeyEnv { get; set; }
        public string? BaseUrl { get; set; }
    }

    class RawModelConfig
    {
        public string? Provider { get; set; }
        public string? ModelId { get; set; }
        public int? MaxTurns { get; set; }
        public RawRetryPolicy? Retry { get; set; }
    }

    class RawRetryPolicy
    {
        public int? MaxRetries { get; set; }
        public double? BaseDelaySeconds { get; set; }
        public double? MaxDelaySeconds { get; set; }
    }
}
