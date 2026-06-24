using Phelix.Core.Agent;

namespace Phelix.Cli;

/// <summary>
/// The static facts about an active session that the CLI surfaces to the user:
/// which model is answering, through which provider, and under which approval mode.
/// </summary>
/// <remarks>
/// Produced by <see cref="PhelixHost.Build"/> from the resolved configuration and
/// passed to <see cref="Program"/> so the startup banner and <c>/status</c> command
/// can render without reaching back into config. Purely a presentation carrier — it
/// holds no behavior and is never persisted.
/// </remarks>
/// <param name="ModelName">The logical model name from config (e.g. <c>"qwen-flash"</c>).</param>
/// <param name="ModelId">The provider-specific model identifier sent to the API.</param>
/// <param name="Provider">The provider key serving this model (e.g. <c>"anthropic"</c>).</param>
/// <param name="Mode">The active approval mode for the session.</param>
internal sealed record SessionInfo(
    string ModelName,
    string ModelId,
    string Provider,
    SessionMode Mode);
