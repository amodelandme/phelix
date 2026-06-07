namespace Phelix.Core.Config;

/// <summary>
/// Thrown when configuration is invalid or required values are missing.
/// </summary>
/// <remarks>
/// Not thrown for an absent config file — absence falls back to
/// <see cref="PhelixConfig.Default"/> without error. Thrown only when a config file
/// is present but its contents fail parsing or validation.
/// </remarks>
public class ConfigException(string message) : Exception(message);
