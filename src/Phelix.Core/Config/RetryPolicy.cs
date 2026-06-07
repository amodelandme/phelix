namespace Phelix.Core.Config;

/// <summary>
/// Controls exponential-backoff retry behaviour for transient model API failures.
/// </summary>
/// <remarks>
/// Applied per-model (via <see cref="ModelConfig.Retry"/>) with a fallback to the global
/// default on <see cref="PhelixConfig.Retry"/>, then to the hardcoded defaults below.
/// </remarks>
public sealed record RetryPolicy
{
    public static readonly RetryPolicy Default = new();

    /// <summary>Maximum number of retry attempts after the initial failure.</summary>
    public int MaxRetries { get; init; } = 4;

    /// <summary>Delay before the first retry. Doubles on each subsequent attempt.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Upper bound on computed delay before jitter is applied.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(60);
}
