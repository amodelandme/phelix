using System.Text.Json.Serialization;

namespace Phelix.Core.Session;

/// <summary>
/// Indicates the outcome of a sensor check for a given turn.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SensorStatus
{
    /// <summary>
    /// The sensor ran and its check succeeded (e.g. build produced no errors, tests passed).
    /// </summary>
    Passed,

    /// <summary>
    /// The sensor ran and its check failed (e.g. build errors were present, tests failed).
    /// </summary>
    Failed,

    /// <summary>
    /// The sensor did not run because its precondition was not met for this turn
    /// (e.g. no files were written, so the build sensor was not applicable).
    /// </summary>
    Skipped
}
