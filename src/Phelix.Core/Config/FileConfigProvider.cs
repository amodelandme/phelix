using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Phelix.Core.Config;

public class FileConfigProvider(string filePath) : IConfigProvider
{
    static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

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
                    MaxTurns = kv.Value.MaxTurns ?? 5
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
            Models = models
        };
    }

    // YamlDotNet target — nullable fields because any key may be absent
    class RawConfig
    {
        public string? ActiveModel { get; set; }
        public string? SystemPrompt { get; set; }
        public Dictionary<string, RawProviderConfig> Providers { get; set; } = [];
        public Dictionary<string, RawModelConfig> Models { get; set; } = [];
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
    }
}
