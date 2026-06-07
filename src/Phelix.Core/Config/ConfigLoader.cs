namespace Phelix.Core.Config;

public static class ConfigLoader
{
    public static PhelixConfig Load()
    {
        string? path = ResolveConfigPath();

        if (path is null)
            return PhelixConfig.Default;

        PhelixConfig config = new FileConfigProvider(path).Load();

        Validate(config);
        WarnMissingApiKeys(config);

        return config;
    }

    static string? ResolveConfigPath()
    {
        string? envPath = Environment.GetEnvironmentVariable("PHELIX_CONFIG");

        if (envPath is not null)
            return envPath;

        string defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".phelix", "config.yaml");

        return File.Exists(defaultPath) ? defaultPath : null;
    }

    static void Validate(PhelixConfig config)
    {
        if (!config.Models.ContainsKey(config.ActiveModel))
            throw new ConfigException($"active_model '{config.ActiveModel}' is not defined in models.");

        foreach ((string modelName, ModelConfig model) in config.Models)
        {
            if (!config.Providers.ContainsKey(model.Provider))
                throw new ConfigException($"Model '{modelName}' references unknown provider '{model.Provider}'.");
        }
    }

    static void WarnMissingApiKeys(PhelixConfig config)
    {
        foreach ((string providerName, ProviderConfig provider) in config.Providers)
        {
            string? value = Environment.GetEnvironmentVariable(provider.ApiKeyEnv);

            if (string.IsNullOrEmpty(value))
                Console.WriteLine($"Warning: provider '{providerName}' expects env var '{provider.ApiKeyEnv}' which is not set.");
        }
    }
}
