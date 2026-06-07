namespace Phelix.Core.Session;

/// <summary>
/// An immutable record of a single tool invocation within a turn.
/// </summary>
/// <remarks>
/// Stored in <see cref="TurnRecord.ToolCalls"/> and also in <see cref="Agent.Turn.ToolCalls"/>.
/// The <see cref="Result"/> is the truncated string — the full tool output is never stored.
/// </remarks>
public record ToolCallRecord(
    /// <summary>
    /// The tool-call identifier assigned by the model in its request. Used to correlate
    /// this record back to the original tool-call message in the conversation history.
    /// </summary>
    string CallId,

    /// <summary>
    /// The name of the tool the model requested to invoke.
    /// </summary>
    string Name,

    /// <summary>
    /// The raw JSON-serialized arguments as the model supplied them, before any parsing
    /// or validation by the tool implementation.
    /// </summary>
    string ArgumentsJson,

    /// <summary>
    /// The truncated string the model actually received as the tool result, capped at
    /// <c>AgentLoop.MaxToolOutputChars</c>. The full output is never stored.
    /// </summary>
    string Result,

    /// <summary>
    /// Whether the tool was successfully dispatched and returned a result.
    /// </summary>
    ToolCallStatus Status
);
