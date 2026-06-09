namespace Phelix.Core.Config;

/// <summary>
/// The single configuration object passed through the harness at startup.
/// </summary>
/// <remarks>
/// Populated by <see cref="ConfigLoader.Load"/> and then threaded into <c>PhelixHost</c>
/// at startup. Immutable after construction — all properties use <c>init</c>.
/// When no config file is present, <see cref="Default"/> is used without throwing.
/// </remarks>
public record PhelixConfig
{
    /// <summary>
    /// Hardcoded fallback configuration used when no config file exists at
    /// <c>~/.phelix/config.yaml</c>. Targets OpenRouter with <c>qwen-flash</c>.
    /// </summary>
    public static readonly PhelixConfig Default = new()
    {
        ActiveModel = "qwen-flash",
        SystemPrompt = """
            You are Phelix, an advanced CLI-driven agentic developer harness for .NET. CORE BEHAVIORAL PROTOCOLS:
            1. TOOL-FIRST EXECUTION: When given a task, prefer direct tool execution over conversational summaries. If you lack context, immediately invoke a file discovery or analysis tool.
            2. CONTEXT PRESERVATION: Minimize token output. Keep text explanations sparse, direct, and high-density. Avoid conversational filler or introductory pleasantries.
            3. IN-PLANE BOUNDARIES: Adhere strictly to any persona rules, formatting constraints, or file patterns provided dynamically via role injection files (e.g., AGENTS.md).
            4. FAIL FAST: If a tool execution fails or code does not compile via validation gates, stop immediately, process the raw error diagnostic, and formulate a direct fix.
            """,
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

    /// <summary>
    /// Key into <see cref="Models"/> identifying which model to use for the session.
    /// Must match an entry in <see cref="Models"/> or <see cref="ConfigLoader"/> throws
    /// a <see cref="ConfigException"/> during validation.
    /// </summary>
    public required string ActiveModel { get; init; }

    /// <summary>
    /// Injected as the <c>Instructions</c> field on every <c>ChatOptions</c> instance,
    /// defining the agent's role and constraints for the entire session.
    /// </summary>
    public required string SystemPrompt { get; init; }

    /// <summary>
    /// Named provider configurations keyed by provider name (e.g. <c>"openrouter"</c>).
    /// Each <see cref="ModelConfig"/> references one of these by name.
    /// </summary>
    public required IReadOnlyDictionary<string, ProviderConfig> Providers { get; init; }

    /// <summary>
    /// Named model configurations keyed by model name (e.g. <c>"qwen-flash"</c>).
    /// <see cref="ActiveModel"/> must match one of these keys.
    /// </summary>
    public required IReadOnlyDictionary<string, ModelConfig> Models { get; init; }

    /// <summary>
    /// Global retry policy applied to all models that do not specify their own
    /// <see cref="ModelConfig.Retry"/> override. Falls back to <see cref="RetryPolicy.Default"/>
    /// when <c>null</c>.
    /// </summary>
    public RetryPolicy? Retry { get; init; }
}
