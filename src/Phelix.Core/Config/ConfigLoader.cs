namespace Phelix.Core.Config;

/// <summary>
/// Single entry point for loading Phelix configuration at startup.
/// </summary>
/// <remarks>
/// Orchestrates path resolution, file loading via <see cref="FileConfigProvider"/>,
/// cross-reference validation, and API key presence warnings. Callers should use
/// <see cref="Load"/> directly — there is no need to instantiate this class.
/// </remarks>
public static class ConfigLoader
{
    /// <summary>
    /// Loads, validates, and returns the active <see cref="PhelixConfig"/>.
    /// </summary>
    /// <returns>
    /// A fully validated <see cref="PhelixConfig"/>. Returns <see cref="PhelixConfig.Default"/>
    /// when no config file is found — absence is not an error.
    /// </returns>
    /// <exception cref="ConfigException">
    /// Thrown when a config file exists but is malformed or fails cross-reference
    /// validation (e.g. <c>active_model</c> references a model not in <c>models</c>).
    /// </exception>
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

    /// <summary>
    /// Resolves the effective <see cref="RetryPolicy"/> for <paramref name="model"/>
    /// using the three-level fallback: model override → global default → hardcoded defaults.
    /// </summary>
    public static RetryPolicy ResolveRetryPolicy(PhelixConfig config, ModelConfig model) =>
        model.Retry ?? config.Retry ?? RetryPolicy.Default;

    /// <summary>
    /// Resolves the config file path to use for this run.
    /// </summary>
    /// <returns>
    /// The path from <c>PHELIX_CONFIG</c> if set; otherwise <c>~/.phelix/config.yaml</c>
    /// if that file exists; otherwise <c>null</c>, which causes <see cref="Load"/> to
    /// fall back to <see cref="PhelixConfig.Default"/>.
    /// </returns>
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

    /// <summary>
    /// Validates cross-references within a loaded config.
    /// </summary>
    /// <param name="config">The config to validate.</param>
    /// <exception cref="ConfigException">
    /// Thrown when <see cref="PhelixConfig.ActiveModel"/> is not a key in
    /// <see cref="PhelixConfig.Models"/>, or when any model references a provider
    /// not present in <see cref="PhelixConfig.Providers"/>.
    /// </exception>
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

    /// <summary>
    /// Writes a warning to <see cref="Console.Error"/> for each provider whose API key
    /// environment variable is not set. Does not throw.
    /// </summary>
    /// <param name="config">The validated config whose providers to check.</param>
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
