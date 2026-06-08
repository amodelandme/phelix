using Phelix.Core.Session;

namespace Phelix.Core.Agent;

/// <summary>
/// Per-turn async callbacks that fire at well-defined moments in the agent loop.
/// </summary>
/// <remarks>
/// Passed to <see cref="AgentLoop.RunTurnAsync"/> on each call; not stored on the
/// session. All delegates are nullable — omit any callback that the caller does not
/// need. <see cref="AgentOptions"/> remains for session-level configuration (model ID,
/// system prompt, max turns); callbacks are a per-turn concern and do not belong there.
/// </remarks>
/// <param name="OnChunk">
/// Invoked with each streamed text fragment on the final (non-tool-call) model response.
/// </param>
/// <param name="OnToolStarted">
/// Invoked immediately before a tool executes, with the tool's name and resolved arguments.
/// </param>
/// <param name="OnToolCompleted">
/// Invoked immediately after a tool finishes, with its name, outcome, and wall-clock duration.
/// </param>
public readonly record struct TurnCallbacks(
    Func<string, Task>? OnChunk = null,
    Func<string, IReadOnlyDictionary<string, object?>, Task>? OnToolStarted = null,
    Func<string, ToolCallStatus, TimeSpan, Task>? OnToolCompleted = null
);
