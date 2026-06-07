using System.Text.Json.Serialization;

namespace Phelix.Core.Session;

/// <summary>
/// Base record for structured events attached to a turn in the session log.
/// </summary>
/// <remarks>
/// <c>TurnEvent</c> is the reserved extension point for Phase 3 sensor results. It is not
/// yet populated by the harness — no code currently appends events to a turn record.
/// The <see cref="JsonPolymorphicAttribute"/> enables type-safe deserialization of derived
/// event types from the session log without switching on a string discriminator manually.
/// <see cref="SensorResultEvent"/> is the only concrete type today; additional sensor event
/// types will be added in Phase 3. Do not use <c>TurnEvent</c> as a general event bus —
/// it exists solely for sensor feedback.
/// </remarks>
/// <param name="Timestamp">UTC timestamp of when the event was recorded.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SensorResultEvent), "sensorResult")]
public abstract record TurnEvent(DateTimeOffset Timestamp);

/// <summary>
/// A sensor result event recording the outcome of a single sensor check for a turn.
/// </summary>
/// <param name="Timestamp">UTC timestamp of when the sensor result was recorded.</param>
/// <param name="SensorName">The name of the sensor that produced this result.</param>
/// <param name="Output">The raw output text produced by the sensor (e.g. build output, test summary).</param>
/// <param name="Status">Whether the sensor check passed, failed, or was skipped.</param>
public sealed record SensorResultEvent(
    DateTimeOffset Timestamp,
    string SensorName,
    string Output,
    SensorStatus Status
) : TurnEvent(Timestamp);
