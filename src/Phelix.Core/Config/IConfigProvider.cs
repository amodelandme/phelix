namespace Phelix.Core.Config;

/// <summary>
/// Seam between config loading and the rest of the harness.
/// </summary>
/// <remarks>
/// <see cref="FileConfigProvider"/> is the production implementation that reads
/// <c>~/.phelix/config.yaml</c>. Tests or a future TUI can supply alternative
/// implementations without touching the harness internals.
/// Implementations must throw <see cref="ConfigException"/> on invalid or missing
/// required config — returning partial state is not permitted.
/// </remarks>
public interface IConfigProvider
{
    /// <summary>
    /// Loads and returns a fully validated <see cref="PhelixConfig"/>.
    /// </summary>
    /// <returns>A fully populated, valid <see cref="PhelixConfig"/>.</returns>
    /// <exception cref="ConfigException">
    /// Thrown when config is invalid, malformed, or required values are absent.
    /// </exception>
    PhelixConfig Load();
}
