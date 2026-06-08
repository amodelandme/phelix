using System.Text.Json.Serialization;

namespace Phelix.Core.Agent;

/// <summary>
/// Describes why an agent turn stopped executing.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TurnExitReason
{
    /// <summary>The model returned a natural stop — text response with no pending tool calls.</summary>
    Completed,

    /// <summary>The turn was halted because <see cref="AgentOptions.MaxTurns"/> was reached.</summary>
    TurnLimitReached,

    /// <summary>The turn ended due to an unhandled exception. History is unchanged; the error is logged.</summary>
    Error,
}
