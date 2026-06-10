using System.Text.Json.Serialization;

namespace Phelix.Core.Session;

/// <summary>
/// Indicates whether a tool invocation was successfully dispatched.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ToolCallStatus
{
    /// <summary>
    /// A registered tool matched the requested name, <c>ExecuteAsync</c> was called, and
    /// it returned a result string. Note: the result string itself may describe an error
    /// from within the tool — <c>Succeeded</c> means the dispatch succeeded, not that the
    /// tool's operation succeeded.
    /// </summary>
    Succeeded,

    /// <summary>
    /// No registered tool matched the requested name. <c>ExecuteAsync</c> was never called
    /// and the model received an error notice instead of a tool result.
    /// </summary>
    Failed,

    /// <summary>
    /// The tool was found and valid, but the user declined the approval prompt.
    /// <c>ExecuteAsync</c> was never called. The model received a denial notice.
    /// </summary>
    Denied
}
